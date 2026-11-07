namespace RadioHomeEngine

open System
open System.Diagnostics
open System.IO
open System.Threading
open System.Threading.Tasks
open System.Threading.Channels
open FSharp.Control
open System.Collections.Generic

type ShellSegmentSourceFactory(displayName: string, inputs: RawAudioInput list) =
    let segmentTimeSeconds = 5
    let hlsBitrate = 320 * 1024

    let quietParameters = "-nostats -hide_banner -loglevel warning"
    let formatParameters = "-f s16le -ar 44100 -ac 2"
    let hlsParameters = $"-f hls -hls_time {segmentTimeSeconds} -hls_segment_type mpegts -hls_flags program_date_time+temp_file -c:a aac -ac 2 -b:a 327680"

    let zeroes = new ReadOnlyMemory<byte>(Array.zeroCreate<byte> 81920)

    let defaultProgram = Program "Unknown"
    let mutable currentProgram = defaultProgram

    let generateScriptAsync (tempFolder: RawAudioTemporaryFolder) = asyncSeq {
        Console.Error.WriteLine($"[ShellSegmentSourceFactory] Script generation started")

        "PATH=$PATH:$(pwd)"

        let waitFor (command: string) = asyncSeq {
            let sentinel = $"{Guid.NewGuid()}"

            let realPath = tempFolder.GetPath(RealPath, sentinel)
            let unixPath = tempFolder.GetPath(UnixPath, sentinel)

            use fsw = tempFolder.CreateFileSystemWatcher()

            $"( {command} ) < /dev/null"
            $"touch {unixPath}"

            while not (File.Exists(realPath)) do
                let! args = Async.AwaitEvent fsw.Created
                ignore args
        }

        let! cancellationToken = Async.CancellationToken

        let getTracks scope = [
            for info in CD.getDriveInfo scope do
                match info.disc.audio with
                | None -> ()
                | Some audioDisc ->
                    for track in audioDisc.tracks do
                        (info.device, audioDisc, track.position)
        ]

        let playCD drive = asyncSeq {
            match DiscDriveStatus.tryGetAudioDiscInfo drive with
            | Some audioDisc when not (List.isEmpty audioDisc.tracks) ->
                currentProgram <- Song {
                    Title = String.concat " / " audioDisc.titles
                    Artists = audioDisc.artists
                    Albums = []
                    Images = []
                }

                let devicePath = DiscDrive.getPath drive
                let trackCount = List.length audioDisc.tracks
                yield! waitFor $"icedax -D {devicePath} -t 1+{trackCount} - | ffmpeg {quietParameters} -f wav -i - {formatParameters} -"

            | _ -> ()
        }

        let playTrack (drive, audioDisc, trackNumber) = asyncSeq {
            let trackTitle =
                audioDisc.tracks
                |> Seq.where (fun t -> t.position = trackNumber)
                |> Seq.map (fun t -> t.title)
                |> Seq.tryHead

            match trackTitle with
            | None -> ()
            | Some title ->
                currentProgram <- Song {
                    Title = title
                    Artists = audioDisc.artists
                    Albums = audioDisc.titles
                    Images = []
                }

                let devicePath = DiscDrive.getPath drive
                yield! waitFor $"icedax -D {devicePath} -t {trackNumber} - | ffmpeg {quietParameters} -f wav -i - {formatParameters} -"
        }

        let playFile (target: string) = asyncSeq {
            let tempFileName = $"{Guid.NewGuid()}{Path.GetExtension(target)}"

            let realPath = tempFolder.GetPath(RealPath, tempFileName)
            let unixPath = tempFolder.GetPath(UnixPath, tempFileName)

            ignore (File.CreateSymbolicLink(realPath, target))

            let! info = Probe.getAudioFileInfoAsync realPath cancellationToken |> Async.AwaitTask

            currentProgram <- info

            yield! waitFor $"ffmpeg {quietParameters} -i {unixPath} {formatParameters} -"

            File.Delete(realPath)
        }

        let shuffle (getItems: unit -> 'T seq) (play: 'T -> IAsyncEnumerable<string>) = asyncSeq {
            let mutable active = true
            let mutable recent = []

            let isFresh x = not (List.contains x recent)

            while active do
                let selectedTrack =
                    getItems ()
                    |> Seq.where isFresh
                    |> Seq.randomShuffle
                    |> Seq.tryHead

                match selectedTrack with
                | None ->
                    let halfway = List.length recent / 2
                    recent <- recent |> List.truncate halfway
                    if recent = [] then
                        active <- false
                | Some selected ->
                    yield! play selected
                    recent <- selected :: recent
        }

        for input in inputs do
            currentProgram <- defaultProgram

            match input with
            | AudioCDs (scope, AllTracks) ->
                for (drive, audioDisc, trackNumber) in getTracks scope do
                    let currentAudioDiscInfo = DiscDriveStatus.tryGetAudioDiscInfo drive
                    if currentAudioDiscInfo = Some audioDisc then
                        yield! playTrack (drive, audioDisc, trackNumber)

            | AudioCDs (scope, EntireDisc) ->
                for (drive, _, _) in getTracks scope do
                    yield! playCD drive

            | AudioCDs (scope, ShuffleCD) ->
                yield! shuffle (fun () -> getTracks scope) (fun t -> playTrack t)

            | AudioCDTrack (drive, audioDisc, trackNumber) ->
                yield! playTrack (drive, audioDisc, trackNumber)

            | AudioFile target ->
                yield! playFile target

            | BrownNoise duration ->
                currentProgram <- Program "Brown noise"

                let durationStr =
                    match duration with
                    | Infinite -> ""
                    | Finite ts -> $"-t {ts.TotalSeconds}"

                yield! waitFor $"ffmpeg {quietParameters} -f lavfi -i anoisesrc=sample_rate=44100:color=brown {durationStr} {formatParameters} -"

            | Forecast ->
                let! forecasts = Weather.getForecastsAsync cancellationToken |> Async.AwaitTask
                for forecast in forecasts |> Seq.take 3 do
                    currentProgram <- Program forecast.screen

                    let tempFileName = $"{Guid.NewGuid()}.txt"

                    let realPath = tempFolder.GetPath(RealPath, tempFileName)
                    let unixPath = tempFolder.GetPath(UnixPath, tempFileName)

                    do! File.WriteAllTextAsync(realPath, forecast.speech, cancellationToken) |> Async.AwaitTask

                    yield! waitFor $"espeak -f {unixPath} --stdout -v us-mbrola-2 -s 140 -a 150 | ffmpeg {quietParameters} -f wav -i - {formatParameters} -"

            | Shuffle files ->
                yield! shuffle (fun () -> files) (fun f -> playFile f)

            | Silence duration ->
                currentProgram <- Program "Silence"

                let durationStr =
                    match duration with
                    | Infinite -> ""
                    | Finite ts -> $"-t {ts.TotalSeconds}"

                yield! waitFor $"ffmpeg {quietParameters} -f lavfi -i anullsrc=cl=stereo:sample_rate=44100 {durationStr} {formatParameters} -"

            | SpeechSynthesis (display, text) ->
                currentProgram <- Program display

                let tempFileName = $"{Guid.NewGuid()}.txt"

                let realPath = tempFolder.GetPath(RealPath, tempFileName)
                let unixPath = tempFolder.GetPath(UnixPath, tempFileName)

                do! File.WriteAllTextAsync(realPath, text, cancellationToken) |> Async.AwaitTask

                yield! waitFor $"espeak -f {unixPath} --stdout -v us-mbrola-2 -s 140 -a 150 | ffmpeg {quietParameters} -f wav -i - {formatParameters} -"

            | RemoteStream source ->
                if source.Url.Contains('\'') then raise (new NotImplementedException())

                yield! waitFor $"ffmpeg {quietParameters} -i '{source.Url}' {formatParameters} -"

            | WWVSimulator ->
                yield! waitFor $"wwvsim -r 44100 | ffmpeg {quietParameters} -f s16le -ar 44100 -ac 1 -i - {formatParameters} -"

        "sleep 1"

        currentProgram <- defaultProgram
        Console.Error.WriteLine($"[ShellSegmentSourceFactory] Script generation ended")
    }

    interface IProgramInformationSource with
        member this.GetCurrentProgramInformationAsync(_) = Task.FromResult({
            ChannelName = displayName
            Program = currentProgram
        })

    interface ISegmentSourceFactory with
        member this.ChannelInformation = {
            ChannelName = displayName
            ChannelDescriptions = []
        }

        member this.ProgramInformationSource = this

        member this.CreateAsync() = task {
            let cancellationTokenSource = new CancellationTokenSource()
            let cancellationToken = cancellationTokenSource.Token

            let bash =
                if Environment.OSVersion.Platform = PlatformID.Win32NT
                then "C:/Program Files/Git/bin/bash.exe"
                else "bash"

            let tempFolder = RawAudioTemporaryFolder.Create()

            let generator =
                new ProcessStartInfo(
                    bash,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true)
                |> Process.Start

            let control = task {
                printfn "[ShellSegmentSourceFactory] Starting shell pipe"

                use input = generator.StandardInput

                let iterationWorkflow =
                    generateScriptAsync tempFolder
                    |> AsyncSeq.iterAsync (fun item -> input.WriteLineAsync(item.AsMemory(), cancellationToken) |> Async.AwaitTask)

                do! Async.StartAsTask(
                    iterationWorkflow,
                    cancellationToken = cancellationToken)

                printfn "[ShellSegmentSourceFactory] Ending shell pipe"
            }

            let realChunklistPath = tempFolder.GetPath(RealPath, "chunklist.m3u8")

            let reencoder = Process.Start(new ProcessStartInfo(
                "ffmpeg",
                $"{quietParameters} -readrate 1 -readrate_catchup 2 {formatParameters} -i - {hlsParameters} {realChunklistPath}",
                RedirectStandardInput = true))

            let channel = Channel.CreateBounded(3)

            let mutable allDataWritten = false
            let mutable allSegmentsRead = false

            let pipe = task {
                printfn "[ShellSegmentSourceFactory] Starting pipe"

                // We manually pipe data between these two processes so that:
                // 1. We can pause audio generation while we read generated segments into memory and delete them from the temporary folder
                // 2. We can end the encoder's stdin when this object is disposed
                // 3. We can add extra silence to the end of the audio data until a full segment is generated

                try
                    use input = generator.StandardOutput.BaseStream
                    use output = reencoder.StandardInput.BaseStream

                    let buffer = Array.zeroCreate<byte> 81920

                    while not allSegmentsRead && not cancellationToken.IsCancellationRequested do
                        let! len = input.ReadAsync(buffer, cancellationToken)
                        let streamFinished = len = 0

                        if streamFinished then
                            allDataWritten <- true
                            do! output.WriteAsync(zeroes, cancellationToken)
                        else
                            do! output.WriteAsync(buffer, 0, len, cancellationToken)
                with
                | :? IOException when cancellationToken.IsCancellationRequested -> ()
                | ex -> printfn "%A" ex

                channel.Writer.Complete()
                printfn "[ShellSegmentSourceFactory] Ending pipe"
            }

            let mutable mediaSequence = new UInt128(0UL, 500UL)

            let read = task {
                use fsw = tempFolder.CreateFileSystemWatcher()

                while not allSegmentsRead do
                    let! args = Async.StartAsTask(
                        Async.AwaitEvent fsw.Created,
                        cancellationToken = cancellationToken)
                    ignore args

                    do! Task.Delay(250, cancellationToken)

                    let mutable filesRead = []

                    let filesFound =
                        tempFolder.EnumerateSegments()
                        |> Seq.map FileInfo
                        |> Seq.sortBy (fun i -> i.CreationTime)

                    for info in filesFound do
                        let time = new DateTimeOffset(info.CreationTime)
                        let! data = File.ReadAllBytesAsync(info.FullName, cancellationToken)
                        let segment = {
                            dateTime = new DateTimeOffset(info.CreationTime)
                            duration = segmentTimeSeconds
                            data = data
                            mediaSequence = mediaSequence
                            important = true
                        }
                        mediaSequence <- mediaSequence + UInt128.One
                        do! channel.Writer.WriteAsync(segment, cancellationToken)
                        filesRead <- info.FullName :: filesRead

                    for file in filesRead do
                        File.Delete(file)

                    if allDataWritten then
                        allSegmentsRead <- true
            }

            return {
                new ISegmentSource with
                    member _.ProgramInformationSource = this

                    member _.ReadNextAsync(cancellationToken) = task {
                        try
                            let! readable = channel.Reader.WaitToReadAsync(cancellationToken)
                            if readable then
                                let! segment = channel.Reader.ReadAsync(cancellationToken)
                                return NextSegment segment
                            else
                                return NoMoreSegments
                        with :? OperationCanceledException when cancellationToken.IsCancellationRequested ->
                            printfn "[ShellSegmentSourceFactory] Cancelled"
                            return NoMoreSegments
                    }

                    member _.DisposeAsync() = new ValueTask(task {
                        printfn "[ShellSegmentSourceFactory] Disposing"

                        cancellationTokenSource.Cancel()

                        do! control
                        do! pipe

                        do! generator.WaitForExitAsync()
                        do! reencoder.WaitForExitAsync()

                        let disposables: IDisposable list = [
                            control
                            pipe
                            generator
                            reencoder
                            tempFolder
                        ]

                        for d in disposables do d.Dispose()
                    })
            }
    }
