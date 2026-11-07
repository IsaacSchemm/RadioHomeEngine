namespace RadioHomeEngine

open System
open System.Diagnostics
open System.IO
open System.Threading
open System.Threading.Tasks
open System.Threading.Channels
open FSharp.Control

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

        "set -e"

        "PATH=$PATH:$(pwd)"

        let waitFor (command: string) = asyncSeq {
            let sentinel = $"{Guid.NewGuid()}"

            let realPath = tempFolder.GetPath(RealPath, SpecificFile sentinel)
            let unixPath = tempFolder.GetPath(UnixPath, SpecificFile sentinel)

            ignore (File.Create(realPath))

            $"( {command} ) </dev/null"
            $"rm {unixPath}"

            while File.Exists(realPath) do
                do! Async.Sleep(500)
        }

        let! cancellationToken = Async.CancellationToken

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
                yield! waitFor $"icedax -D {devicePath} -t 1+{trackCount} - | ffmpeg {quietParameters} -f wav -i {formatParameters} -"

            | _ -> ()
        }

        let playTrack drive trackNumber = asyncSeq {
            match DiscDriveStatus.tryGetAudioDiscInfo drive with
            | None -> ()
            | Some audioDisc ->
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
                    yield! waitFor $"icedax -D {devicePath} -t {trackNumber} - | ffmpeg {quietParameters} -f wav -i {formatParameters} -"
        }

        let playFile (target: string) = asyncSeq {
            let tempFileName = $"{Guid.NewGuid()}{Path.GetExtension(target)}"

            let realPath = tempFolder.GetPath(RealPath, SpecificFile tempFileName)
            let unixPath = tempFolder.GetPath(UnixPath, SpecificFile tempFileName)

            ignore (File.CreateSymbolicLink(realPath, target))

            let! info = Probe.getAudioFileInfoAsync realPath cancellationToken |> Async.AwaitTask

            currentProgram <- info

            yield! waitFor $"ffmpeg {quietParameters} -i {unixPath} {formatParameters} -"

            File.Delete(realPath)
        }

        for input in inputs do
            currentProgram <- defaultProgram

            match input with
            | AudioCD drive ->
                yield! playCD drive

            | AudioCDTrack (drive, _, trackNumber) ->
                yield! playTrack drive trackNumber

            | AudioFile target ->
                yield! playFile target

            | BrownNoise duration ->
                currentProgram <- Program "Brown noise"

                let durationStr =
                    match duration with
                    | Infinite -> ""
                    | Finite ts -> $"-t {ts.TotalSeconds}"

                yield! waitFor $"ffmpeg {quietParameters} -f lavfi -i anoisesrc=sample_rate=44100:color=brown {durationStr} {formatParameters} -"

            | Unresolved Forecast ->
                let! forecasts = Weather.getForecastsAsync cancellationToken |> Async.AwaitTask
                for forecast in forecasts |> Seq.take 3 do
                    currentProgram <- Program forecast.screen

                    let tempFileName = $"{Guid.NewGuid()}.txt"

                    let realPath = tempFolder.GetPath(RealPath, SpecificFile tempFileName)
                    let unixPath = tempFolder.GetPath(UnixPath, SpecificFile tempFileName)

                    do! File.WriteAllTextAsync(realPath, forecast.speech, cancellationToken) |> Async.AwaitTask

                    yield! waitFor $"espeak -f {unixPath} --stdout -v us-mbrola-2 -s 140 -a 150 | ffmpeg {quietParameters} -f wav -i - {formatParameters} -"

            | Unresolved (PlayAudioCDs scope) ->
                for drive in DiscDrive.getDevices scope do
                    yield! playCD drive

            | Unresolved (PlayAudioCDTracks scope) ->
                let discs =
                    DiscDrive.getDevices scope
                    |> Seq.choose DiscDriveStatus.tryGetAudioDiscInfo

                let tracks = [
                    for drive in DiscDrive.getDevices scope do
                        match DiscDriveStatus.tryGetAudioDiscInfo drive with
                        | None -> ()
                        | Some audioDisc ->
                            for track in audioDisc.tracks do
                                (drive, audioDisc, track)
                ]

                for (drive, audioDisc, track) in tracks do
                    let currentAudioDiscInfo = DiscDriveStatus.tryGetAudioDiscInfo drive
                    if currentAudioDiscInfo <> Some audioDisc then
                        "exit"

                    yield! playTrack drive track.position

            | Unresolved (ShuffleAudioCDTracks scope) ->
                let discs =
                    DiscDrive.getDevices scope
                    |> Seq.choose DiscDriveStatus.tryGetAudioDiscInfo

                let tracks = [
                    for drive in DiscDrive.getDevices scope do
                        match DiscDriveStatus.tryGetAudioDiscInfo drive with
                        | None -> ()
                        | Some audioDisc ->
                            for track in audioDisc.tracks do
                                (drive, audioDisc, track)
                ]

                for (drive, audioDisc, track) in List.randomShuffle tracks do
                    let currentAudioDiscInfo = DiscDriveStatus.tryGetAudioDiscInfo drive
                    if currentAudioDiscInfo <> Some audioDisc then
                        "exit"

                    yield! playTrack drive track.position

            | Unresolved (ShuffleAudioFiles files) ->
                for file in List.randomShuffle files do
                    if not (File.Exists(file)) then
                        "exit"

                    yield! playFile file

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

                let realPath = tempFolder.GetPath(RealPath, SpecificFile tempFileName)
                let unixPath = tempFolder.GetPath(UnixPath, SpecificFile tempFileName)

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
                printfn "[ShellSegmentSourceFactory] Starting control"

                use input = generator.StandardInput

                let enumerable = generateScriptAsync tempFolder
                let enumerator = enumerable.GetAsyncEnumerator(cancellationToken)

                let mutable active = true
                while active do
                    let! more = enumerator.MoveNextAsync()
                    active <- more && enumerator.Current <> "exit"

                    if active then
                        printfn "> %s" enumerator.Current
                        do! input.WriteLineAsync(enumerator.Current.AsMemory(), cancellationToken)

                printfn "[ShellSegmentSourceFactory] Ending control"
            }

            let realChunklistPath = tempFolder.GetPath(RealPath, SpecificFile "chunklist.m3u8")

            let reencoder = Process.Start(new ProcessStartInfo(
                "ffmpeg",
                $"{quietParameters} -readrate 1 -readrate_catchup 2 {formatParameters} -i - {hlsParameters} {realChunklistPath}",
                RedirectStandardInput = true))

            let channel = Channel.CreateBounded(3)

            let pipe = task {
                printfn "[ShellSegmentSourceFactory] Starting pipe"

                // We manually pipe data between these two processes so that:
                // 1. We can pause audio generation while we read generated segments into memory and delete them from the temporary folder
                // 2. We can end the encoder's stdin when this object is disposed
                // 3. We can add extra silence to the end of the audio data until a full segment is generated

                try
                    let mutable mediaSequence = new UInt128(0UL, 500UL)

                    use control = generator.StandardInput
                    use input = generator.StandardOutput.BaseStream
                    use output = reencoder.StandardInput.BaseStream

                    let buffer = Array.zeroCreate<byte> 81920

                    let mutable nextFileCheck = DateTimeOffset.UtcNow

                    let mutable allSegmentsRead = false

                    while not allSegmentsRead && not cancellationToken.IsCancellationRequested do
                        let! len = input.ReadAsync(buffer, cancellationToken)
                        let streamFinished = len = 0

                        if streamFinished then
                            do! output.WriteAsync(zeroes, cancellationToken)
                        else
                            do! output.WriteAsync(buffer, 0, len, cancellationToken)

                        if nextFileCheck <= DateTimeOffset.UtcNow then
                            nextFileCheck <- nextFileCheck + TimeSpan.FromSeconds(1.0)

                            let mutable filesRead = []

                            let filesFound =
                                tempFolder.EnumerateSegments()
                                |> Seq.map FileInfo
                                |> Seq.sortBy (fun i -> i.CreationTime)

                            for info in filesFound do
                                let time = new DateTimeOffset(info.CreationTime)
                                let age = DateTimeOffset.UtcNow - time
                                if age > TimeSpan.FromSeconds(0.25) then
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

                            if streamFinished && filesRead <> [] then
                                allSegmentsRead <- true
                with
                | :? IOException when cancellationToken.IsCancellationRequested -> ()
                | ex -> printfn "%A" ex

                channel.Writer.Complete()
                printfn "[ShellSegmentSourceFactory] Ending pipe"
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
                        generator.Dispose()

                        do! reencoder.WaitForExitAsync()
                        reencoder.Dispose()

                        tempFolder.Delete()
                    })
            }
    }
