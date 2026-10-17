namespace RadioHomeEngine

open System
open System.Diagnostics
open System.IO
open System.Threading
open System.Threading.Tasks

type NoiseSource() =
    let path = Path.Combine([|
        Path.GetTempPath()
        $"RadioHomeEngine-Noise-{Guid.NewGuid()}"
    |])

    let bitsPerSecond = 65536
    let color = "brown"
    let sampleRate = 44100
    let segmentTimeSeconds = 10.0

    let inputParameters = String.concat " " [
        "-f lavfi"
        $"-i \"anoisesrc=sample_rate={sampleRate}:color={color}\""
    ]

    let pipeFormat = "-f f64le -ac 1"

    let outputParameters = String.concat " " [
        "-f hls"
        $"-hls_time {segmentTimeSeconds}"
        "-hls_segment_type mpegts"
        "-hls_flags program_date_time+temp_file"
        "-c:a aac"
        "-ac 2"
        $"-b:a {bitsPerSecond}"
        Path.Combine(path, "chunklist.m3u8")
    ]

    let readSpeedParameters = String.concat " " [
        "-readrate 1"
    ]

    let mutable generator = null
    let mutable stopGenerator = fun () -> ()
    let mutable reencoder = null
    let mutable pipe = Task.CompletedTask
    let mutable disposing = false

    let startTask = lazy task {
        Directory.CreateDirectory(path) |> ignore

        generator <- Process.Start(new ProcessStartInfo(
            $"ffmpeg",
            $"-nostats -hide_banner -loglevel warning {inputParameters} {pipeFormat} -",
            RedirectStandardInput = true,
            RedirectStandardOutput = true))

        reencoder <- Process.Start(new ProcessStartInfo(
            $"ffmpeg",
            $"-nostats -hide_banner -loglevel warning {readSpeedParameters} {pipeFormat} -i - {outputParameters}",
            RedirectStandardInput = true))

        pipe <- task {
            try
                use input = generator.StandardOutput.BaseStream
                use output = reencoder.StandardInput.BaseStream
                do! input.CopyToAsync(output)
            with :? IOException when disposing -> ()
        }
    }

    interface ISegmentSource with
        member _.ScanAsync(cancellationToken) = task {
            if disposing then
                failwith "[NoiseSource] Already disposed"

            do! startTask.Value

            let mutable segments = []
            let mutable files = []

            for file in Directory.EnumerateFiles(path, "*.ts") do
                let info = new FileInfo(file)
                let time = new DateTimeOffset(info.CreationTime)
                if DateTimeOffset.UtcNow - time > TimeSpan.FromSeconds(0.25) then
                    let! data = File.ReadAllBytesAsync(file, cancellationToken)
                    let segment = {
                        dateTime = new DateTimeOffset(info.CreationTime)
                        duration = segmentTimeSeconds
                        data = data
                        mediaSequence = UInt128.Zero
                    }
                    segments <- segment :: segments
                    files <- file :: files

            for file in files do
                File.Delete(file)

            return segments |> List.sortBy (fun s -> s.dateTime)
        }

    interface IAsyncDisposable with
        member _.DisposeAsync() = new ValueTask(task {
            printfn "[NoiseSource] Disposing"

            disposing <- true

            if not (isNull generator) && not generator.HasExited then
                use inputControl = generator.StandardInput
                do! inputControl.WriteAsync('q')

            if not (isNull generator) then
                do! generator.WaitForExitAsync()

            do! pipe

            if not (isNull reencoder) then
                do! reencoder.WaitForExitAsync()

            Directory.Delete(path, recursive = true)
        })
