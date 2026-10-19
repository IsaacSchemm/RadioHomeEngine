namespace RadioHomeEngine

open System
open System.Diagnostics
open System.IO
open System.Threading
open System.Threading.Channels
open System.Threading.Tasks

type TestSourceFactory() =
    let path = Path.Combine([|
        Path.GetTempPath()
        $"RadioHomeEngine-Test-{Guid.NewGuid()}"
    |])

    let bitsPerSecond = 65536
    let color = "brown"
    let sampleRate = 44100
    let segmentTimeSeconds = 10.0

    let inputParameters = String.concat " " [
        $"-i \"K:/Documents/Music/Steps - Gold Greatest Hits/05 Love’s Got a Hold on My Heart.flac\" -ss 150"
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
                $"-nostats -hide_banner -loglevel warning {readSpeedParameters} {pipeFormat} -i - {outputParameters}",
                RedirectStandardInput = true))

            let pipe = task {
                try
                    use input = generator.StandardOutput.BaseStream
                    use output = reencoder.StandardInput.BaseStream

                    let buffer = Array.zeroCreate<byte> 81920

                    let mutable nextFileCheck = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(1.0)
                    let mutable inputEnded = false
                    let mutable outputEnded = false

                    while not disposing && not outputEnded do
                        let! len = input.ReadAsync(buffer)
                        if len > 0 then
                            do! output.WriteAsync(buffer, 0, len)
                        else
                            inputEnded <- true
                            printfn "[TestSourceFactory] Writing zeroes"
                            do! output.WriteAsync(Array.zeroCreate<byte> 81920)

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

                            if inputEnded && files.Length > 0 then
                                outputEnded <- true
                with :? IOException when disposing -> ()

                printfn "[TestSourceFactory] Completing"
                channel.Writer.Complete()
            }

            return {
                new ISegmentSource with
                    member _.ReadNextAsync(cancellationToken) = task {
                        try
                            let! readable = channel.Reader.WaitToReadAsync(cancellationToken)
                            if readable then
                                let! segment = channel.Reader.ReadAsync(cancellationToken)
                                return NextSegment segment
                            else
                                return NoMoreSegments
                        with :? OperationCanceledException when cancellationToken.IsCancellationRequested ->
                            return NoMoreSegments
                    }

                    member _.DisposeAsync() = new ValueTask(task {
                        if not disposing then
                            printfn "[TestSourceFactory] Disposing"

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
