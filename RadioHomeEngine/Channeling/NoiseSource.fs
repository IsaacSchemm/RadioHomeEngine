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

    let mutable encoder = null

    let startTask = lazy task {
        Directory.CreateDirectory(path) |> ignore

        encoder <- Process.Start(new ProcessStartInfo(
            $"ffmpeg",
            $"{readSpeedParameters} {inputParameters} {outputParameters}",
            RedirectStandardInput = true))
    }

    interface ISegmentSource with
        member _.ScanAsync(cancellationToken) = task {
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
                    }
                    segments <- segment :: segments
                    files <- file :: files

            for file in files do
                File.Delete(file)

            return segments |> List.sortBy (fun s -> s.dateTime)
        }

    interface IAsyncDisposable with
        member _.DisposeAsync() = new ValueTask(task {
            if not (isNull encoder) then
                printfn "[NoiseSource] Disposing"

                if not encoder.HasExited then
                    use inputControl = encoder.StandardInput
                    do! inputControl.WriteAsync('q')

                do! encoder.WaitForExitAsync()

                Directory.Delete(path, recursive = true)
        })
