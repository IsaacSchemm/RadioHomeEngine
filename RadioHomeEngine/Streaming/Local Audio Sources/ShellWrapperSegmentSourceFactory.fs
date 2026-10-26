namespace RadioHomeEngine

open System
open System.Diagnostics
open System.IO
open System.Threading.Tasks
open FSharp.Control
open System.Threading.Channels

type ShellWrapperSegmentSourceFactory(parameters) =
    let shellWrapper = new ShellWrapper(parameters)

    let hlsBitrate = 320 * 1024
    let segmentTimeSeconds = 10.0

    let zeroes = new ReadOnlyMemory<byte>(Array.zeroCreate<byte> 81920)

    let defaultProgramInformationSource = {
        new IProgramInformationSource with
            member _.GetCurrentProgramInformationAsync(_) = Task.FromResult({
                channelName = "Unknown"
                title = "Unknown"
                artists = []
                albums = []
            })
    }

    let mutable programInformationSource = defaultProgramInformationSource

    interface ISegmentSourceFactory with
        member this.ProgramInformationSource = programInformationSource

        member this.CreateAsync() = task {
            let channel = Channel.CreateBounded(3)
            let mutable disposing = false

            let path = Path.Combine([|
                Path.GetTempPath()
                $"RadioHomeEngine-Noise-{Guid.NewGuid()}"
            |])

            let parameters = String.concat " " [
                "-nostats"
                "-hide_banner"
                "-loglevel"; "warning"
                "-readrate"; "1"
                "-f"; "s16le"
                "-ar"; "44100"
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

            Directory.CreateDirectory(path) |> ignore

            let reencoder = Process.Start(new ProcessStartInfo(
                $"ffmpeg",
                parameters,
                RedirectStandardInput = true))

            let pipe = task {
                try
                    let mutable mediaSequence = new UInt128(0UL, 500UL)

                    use output = reencoder.StandardInput.BaseStream

                    let buffer = Array.zeroCreate<byte> 81920
                    let mutable nextFileCheck = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(1.0)

                    use! innerSource = shellWrapper.CreateAsync()
                    programInformationSource <- innerSource

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

                            let infos = [
                                for file in Directory.EnumerateFiles(path, "*.ts") do
                                    new FileInfo(file)
                            ]
                            for info in infos |> Seq.sortBy (fun i -> i.CreationTime) do
                                let time = new DateTimeOffset(info.CreationTime)
                                if DateTimeOffset.UtcNow - time > TimeSpan.FromSeconds(0.25) then
                                    let! data = File.ReadAllBytesAsync(info.FullName)
                                    let segment = {
                                        dateTime = new DateTimeOffset(info.CreationTime)
                                        duration = segmentTimeSeconds
                                        data = data
                                        mediaSequence = mediaSequence
                                        important = true
                                    }
                                    mediaSequence <- mediaSequence + UInt128.One
                                    do! channel.Writer.WriteAsync(segment)
                                    files <- info.FullName :: files

                            for file in files do
                                File.Delete(file)

                            if streamFinished && files <> [] then
                                allSegmentsRead <- true
                with
                | :? IOException when disposing -> ()
                | ex -> printfn "%A" ex

                programInformationSource <- defaultProgramInformationSource

                channel.Writer.Complete()
            }

            return {
                new ISegmentSource with
                    member _.ProgramInformationSource = programInformationSource

                    member _.ReadNextAsync(cancellationToken) = task {
                        try
                            let! readable = channel.Reader.WaitToReadAsync(cancellationToken)
                            if readable then
                                let! segment = channel.Reader.ReadAsync(cancellationToken)
                                return NextSegment segment
                            else
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
