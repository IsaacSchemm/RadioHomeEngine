namespace RadioHomeEngine

open System
open System.Diagnostics
open System.IO
open System.Threading.Tasks
open FSharp.Control
open System.Threading.Channels

type RawAudioSegmentSourceFactory(innerFactory: IRawAudioFactory) =
    let path = Path.Combine([|
        Path.GetTempPath()
        $"RadioHomeEngine-Noise-{Guid.NewGuid()}"
    |])

    let hlsBitrate = 65536
    let segmentTimeSeconds = 2.0

    let parameters = String.concat " " [
        "-nostats"
        "-hide_banner"
        "-loglevel"; "warning"
        "-readrate"; "1"
        "-f"; "f64le"
        "-ac"; "2"
        "-i"; "-"
        "-f"; "hls"
        "-hls_time"; $"{segmentTimeSeconds}"
        "-hls_segment_type"; "mpegts"
        "-hls_flags"; "program_date_time+temp_file"
        "-c:a"; "aac"
        "-ac"; "2"
        "-b:a"; $"{hlsBitrate}"
        Path.Combine(path, "chunklist.m3u8")
    ]

    let zeroes = new ReadOnlyMemory<byte>(Array.zeroCreate<byte> 81920)

    interface IProgramInformationSource with
        member this.GetCurrentProgramInformationAsync(_) = Task.FromResult(innerFactory.ProgramInformation)

    interface ISegmentSourceFactory with
        member this.ProgramInformationSource = this

        member this.CreateAsync() = task {
            let channel = Channel.CreateBounded(3)
            let mutable disposing = false

            Directory.CreateDirectory(path) |> ignore

            let reencoder = Process.Start(new ProcessStartInfo(
                $"ffmpeg",
                parameters,
                RedirectStandardInput = true))

            let pipe = task {
                try
                    use output = reencoder.StandardInput.BaseStream

                    let buffer = Array.zeroCreate<byte> 81920

                    let mutable nextFileCheck = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(1.0)

                    use! innerSource = innerFactory.CreateAsync()
                    use input = innerSource.Stream

                    let mutable allSegmentsRead = false

                    while not disposing && not allSegmentsRead do
                        let! len = input.ReadAsync(buffer)
                        let streamFinished = len = 0

                        if streamFinished then
                            do! output.WriteAsync(zeroes)
                        else
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

                            if streamFinished && files <> [] then
                                allSegmentsRead <- true
                with :? IOException when disposing -> ()

                channel.Writer.Complete()
            }

            return {
                new ISegmentSource with
                    member _.ProgramInformationSource = this

                    member _.ReadNextAsync(cancellationToken) = task {
                        try
                            printfn "[RawAudioSegmentSourceFactory] Waiting to read"
                            let! readable = channel.Reader.WaitToReadAsync(cancellationToken)
                            if readable then
                                printfn "[RawAudioSegmentSourceFactory] Reading"
                                let! segment = channel.Reader.ReadAsync(cancellationToken)
                                return NextSegment segment
                            else
                                printfn "[RawAudioSegmentSourceFactory] Ending"
                                return NoMoreSegments
                        with :? OperationCanceledException when cancellationToken.IsCancellationRequested ->
                            printfn "[RawAudioSegmentSourceFactory] Cancelled"
                            return NoMoreSegments
                    }

                    member _.DisposeAsync() = new ValueTask(task {
                        printfn "[RawAudioSegmentSourceFactory] Disposing"

                        disposing <- true

                        do! pipe

                        if not (isNull reencoder) then
                            do! reencoder.WaitForExitAsync()

                        Directory.Delete(path, recursive = true)
                    })
            }
        }
