namespace RadioHomeEngine

open System
open System.Diagnostics
open System.IO
open System.Threading
open System.Threading.Channels
open System.Threading.Tasks

type NoiseSourceFactory() =
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

    interface ISegmentSourceFactory with
        member _.CreateAsync() = task {
            let channel = Channel.CreateBounded(3)
            let mutable disposing = false

            Directory.CreateDirectory(path) |> ignore

            let generator = Process.Start(new ProcessStartInfo(
                $"ffmpeg",
                $"-nostats -hide_banner -loglevel warning {inputParameters} {pipeFormat} -",
                RedirectStandardInput = true,
                RedirectStandardOutput = true))

            let reencoder = Process.Start(new ProcessStartInfo(
                $"ffmpeg",
                $"{readSpeedParameters} {pipeFormat} -i - {outputParameters}",
                RedirectStandardInput = true))

            let pipe = task {
                try
                    use input = generator.StandardOutput.BaseStream
                    use output = reencoder.StandardInput.BaseStream

                    let buffer = Array.zeroCreate<byte> 81920

                    let mutable nextFileCheck = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(1.0)

                    while not disposing do
                        let! len = input.ReadAsync(buffer)
                        do! output.WriteAsync(buffer, 0, len)

                        if nextFileCheck <= DateTimeOffset.UtcNow then
                            nextFileCheck <- nextFileCheck + TimeSpan.FromSeconds(1.0)

                            let mutable files = []

                            for file in Directory.EnumerateFiles(path, "*.ts") do
                                let info = new FileInfo(file)
                                let time = new DateTimeOffset(info.CreationTime)
                                if DateTimeOffset.UtcNow - time > TimeSpan.FromSeconds(0.25) then
                                    let! data = File.ReadAllBytesAsync(file)
                                    let segment = {
                                        dateTime = new DateTimeOffset(info.CreationTime)
                                        duration = segmentTimeSeconds
                                        data = data
                                        mediaSequence = UInt128.Zero
                                    }
                                    do! channel.Writer.WriteAsync(segment)
                                    files <- file :: files

                            for file in files do
                                File.Delete(file)
                with :? IOException when disposing -> ()

                channel.Writer.Complete()
            }

            return {
                new ILocalSegmentSource with
                    member _.ReadNextAsync(cancellationToken) = task {
                        try
                            printfn "[NoiseSourceFactory] Waiting to read"
                            let! readable = channel.Reader.WaitToReadAsync(cancellationToken)
                            if readable then
                                printfn "[NoiseSourceFactory] Reading"
                                let! segment = channel.Reader.ReadAsync(cancellationToken)
                                return AsyncNextSegment segment
                            else
                                printfn "[NoiseSourceFactory] Ending"
                                return AsyncNoMoreSegments
                        with :? OperationCanceledException when cancellationToken.IsCancellationRequested ->
                            printfn "[NoiseSourceFactory] Cancelled"
                            return AsyncNoMoreSegments
                    }

                    member _.TryReadNext() =
                        match channel.Reader.TryRead() with
                        | (true, segment) -> NextSegment segment
                        | (false, _) when channel.Reader.Completion.IsCompleted -> NoMoreSegments
                        | (false, _) -> WaitingForMoreSegments

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
            }
        }
