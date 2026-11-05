namespace RadioHomeEngine

open System
open System.Diagnostics
open System.IO
open System.Threading
open System.Threading.Tasks
open System.Threading.Channels
open FSharp.Control

type ShellSegmentSourceFactory(displayName: string, initialInputs: RawAudioInput list) =
    let _ = printfn "[ShellSegmentSourceFactory] Creating"

    let segmentTimeSeconds = 5
    let hlsBitrate = 320 * 1024

    let quietParameters = "-nostats -hide_banner -loglevel warning"
    let formatParameters = "-f s16le -ar 44100 -ac 2"
    let hlsParameters = $"-f hls -hls_time {segmentTimeSeconds} -hls_segment_type mpegts -hls_flags program_date_time+temp_file -c:a aac -ac 2 -b:a 327680"

    let zeroes = new ReadOnlyMemory<byte>(Array.zeroCreate<byte> 81920)

    let enumerateTracks scope = [
        for driveInfo in CD.getDriveInfo scope do
            match driveInfo.disc.audio with
            | None -> ()
            | Some audioDiscInfo ->
                for t in audioDiscInfo.tracks do
                    let song = {
                        Title = t.title
                        Artists = audioDiscInfo.artists
                        Albums = audioDiscInfo.titles
                        Images = []
                    }
                    AudioCDTrack (driveInfo.device, audioDiscInfo, t.position)
    ]

    let asyncResolveInputs (inputs: RawAudioInput seq) = asyncSeq {
        let! cancellationToken = Async.CancellationToken

        for input in inputs do
            match input with
            | Unresolved Forecast ->
                let! forecasts = Weather.getForecastsAsync cancellationToken |> Async.AwaitTask
                for f in forecasts do
                    SpeechSynthesis (f.screen, f.speech)

            | Unresolved (PlayAudioCDs scope) ->
                for drive in DiscDrive.getAll () do
                    AudioCD drive

            | Unresolved (PlayAudioCDTracks scope) ->
                yield! enumerateTracks scope

            | Unresolved (ShuffleAudioCDTracks scope) ->
                yield! List.randomShuffle (enumerateTracks scope)

            | Unresolved (ShuffleAudioFiles files) ->
                yield! List.randomShuffle [for file in files do AudioFile file]

            | other ->
                other
    }

    let buildScript (tempFolder: RawAudioTemporaryFolder) (inputs: RawAudioInput seq) = String.concat "\n" [
        "#!/bin/sh"
        "set -e"

        "PATH=$PATH:$(pwd)"

        let eof = $"{Guid.NewGuid()}"

        for (i, input) in Seq.indexed inputs do
            $"echo {i} > {tempFolder.GetPath(UnixPath, CurrentIndex)}"

            match input with
            | AudioCD drive ->
                match DiscDriveStatus.tryGetAudioDiscInfo drive with
                | Some audioDisc when not (List.isEmpty audioDisc.tracks) ->
                    String.concat " " [
                        "icedax"
                        $"-D {DiscDrive.getPath drive}"
                        $"-t 1+{List.length audioDisc.tracks}"
                        "-"
                        "|"
                        "ffmpeg"
                        quietParameters
                        "-f wav"
                        "-i -"
                        formatParameters
                        "-"
                    ]
                | _ -> ()

            | AudioCDTrack (drive, _, trackNumber) ->
                File.WriteAllBytes(
                    tempFolder.GetPath(RealPath, DiscsOk),
                    Array.empty)

                String.concat " " [
                    "icedax"
                    $"-D {DiscDrive.getPath drive}"
                    $"-t {trackNumber}"
                    "-"
                    "|"
                    "ffmpeg"
                    quietParameters
                    "-f wav"
                    "-i -"
                    formatParameters
                    "-"
                ]

                $"if ! [ -f {tempFolder.GetPath(UnixPath, DiscsOk)} ];then exit; fi"

            | AudioFile target ->
                let tempFileName = $"{Guid.NewGuid()}{Path.GetExtension(target)}"

                let _ = File.CreateSymbolicLink(
                    tempFolder.GetPath(RealPath, SpecificFile tempFileName),
                    target)

                String.concat " " [
                    "ffmpeg"
                    quietParameters
                    $"-i {tempFolder.GetPath(UnixPath, SpecificFile tempFileName)}"
                    formatParameters
                    "-"
                ]

            | BrownNoise duration ->
                String.concat " " [
                    "ffmpeg"
                    quietParameters
                    "-f lavfi"
                    "-i anoisesrc=sample_rate=44100:color=brown"

                    match duration with
                    | Infinite -> ()
                    | Finite ts -> $"-t {ts.TotalSeconds}"

                    formatParameters
                    "-"
                ]

            | Unresolved _ ->
                invalidArg "inputs" "Cannot contain an resolved item; use asyncResolveInputs first"

            | Silence duration ->
                String.concat " " [
                    "ffmpeg"
                    quietParameters
                    "-f lavfi"
                    "-i anullsrc=cl=stereo:sample_rate=44100"

                    match duration with
                    | Infinite -> ()
                    | Finite ts -> $"-t {ts.TotalSeconds}"

                    formatParameters
                    "-"
                ]

            | SpeechSynthesis (_, text) ->
                if Environment.OSVersion.Platform <> PlatformID.Win32NT then
                    let textFileName = $"{Guid.NewGuid()}.txt"
                    File.WriteAllText(textFileName, text)

                    String.concat " " [
                        "espeak"
                        $"-f {textFileName}"
                        "--stdout"
                        "-v us-mbrola-2"
                        "-s 140"
                        "-a 150"
                        "|"
                        "ffmpeg"
                        quietParameters
                        "-f wav"
                        "-i -"
                        formatParameters
                        "-"
                    ]

                    $"rm {textFileName}"

            | RemoteStream source ->
                if source.Url.Contains('\'') then raise (new NotImplementedException())

                String.concat " " [
                    "ffmpeg"
                    quietParameters
                    $"-i '{source.Url}'"
                    formatParameters
                    "-"
                ]

            | WWVSimulator ->
                String.concat " " [
                    "wwvsim"
                    "-r 44100"
                    "|"
                    "ffmpeg"
                    quietParameters
                    "-f s16le"
                    "-ar 44100"
                    "-ac 1"
                    "-i -"
                    formatParameters
                    "-"
                ]

        $"echo -1 > {tempFolder.GetPath(UnixPath, CurrentIndex)}"
    ]

    let getProgramInformationAsync input cancellationToken = task {
        let forName str = {
            ChannelName = displayName
            Program = Program str
        }

        match input with
        | AudioCD drive ->
            return forName (String.concat " // " [
                match DiscDriveStatus.tryGetAudioDiscInfo drive with
                | None -> ()
                | Some a -> String.concat " - " [
                    if a.artists <> [] then String.concat ", " a.artists

                    match a.titles with
                    | _ :: _ -> String.concat " / " a.titles
                    | [] -> "Unknown disc"
                ]
            ])

        | AudioCDTrack (_, audioDiscInfo, trackNumber) ->
            let title =
                audioDiscInfo.tracks
                |> Seq.where (fun t -> t.position = trackNumber)
                |> Seq.map (fun t -> t.title)
                |> Seq.tryHead
                |> Option.defaultValue $"Track {trackNumber}"

            return {
                ChannelName = displayName
                Program = Song {
                    Title = title
                    Artists = audioDiscInfo.artists
                    Albums = audioDiscInfo.titles
                    Images = []
                }
            }

        | AudioFile path ->
            let! info = Probe.getAudioFileInfoAsync path cancellationToken
            return {
                ChannelName = displayName
                Program = info
            }

        | BrownNoise _ ->
            return forName "Brown Noise"

        | Unresolved _ ->
            return invalidArg "inputs" "Cannot contain an unresolved item; use asyncResolveInputs first"

        | RemoteStream source ->
            return! source.ProgramInformationSource.GetCurrentProgramInformationAsync(cancellationToken)

        | Silence _ ->
            return forName "Silence"

        | SpeechSynthesis (title, _) ->
            return forName title

        | WWVSimulator ->
            return forName "Time Signal"
    }

    let mutable lastDriveInfo = []
    let mutable resolvedInputs = []
    let mutable inputPointer = -1

    let programInformationSource = {
        new IProgramInformationSource with
            member _.GetCurrentProgramInformationAsync(cancellationToken) = task {
                match resolvedInputs |> List.tryItem inputPointer with
                | None ->
                    return {
                        ChannelName = displayName
                        Program = Program "Unknown"
                    }
                | Some input ->
                    return! getProgramInformationAsync input cancellationToken
            }
    }

    interface ISegmentSourceFactory with
        member this.ChannelInformation = {
            ChannelName = displayName
            ChannelDescriptions = []
        }

        member this.ProgramInformationSource = programInformationSource

        member this.CreateAsync() = task {
            let cancellationTokenSource = new CancellationTokenSource()

            let tempFolder = RawAudioTemporaryFolder.Create()

            lastDriveInfo <- CD.getDriveInfo AllDrives

            let! resolved = Async.StartAsTask(
                AsyncSeq.toListAsync(asyncResolveInputs initialInputs),
                cancellationToken = cancellationTokenSource.Token)

            resolvedInputs <- resolved

            do! File.WriteAllTextAsync(
                tempFolder.GetPath(RealPath, Script),
                buildScript tempFolder resolvedInputs)

            let bash =
                if Environment.OSVersion.Platform = PlatformID.Win32NT
                then "C:/Program Files/Git/bin/bash.exe"
                else "bash"

            let generator =
                new ProcessStartInfo(
                    bash,
                    tempFolder.GetPath(UnixPath, Script),
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true)
                |> Process.Start

            let realChunklistPath = tempFolder.GetPath(RealPath, SpecificFile "chunklist.m3u8")

            let reencoder = Process.Start(new ProcessStartInfo(
                "ffmpeg",
                $"-readrate 1 -readrate_catchup 2 {formatParameters} -i - {hlsParameters} {realChunklistPath}",
                RedirectStandardInput = true))

            let channel = Channel.CreateBounded(3)
            let mutable disposing = false

            let pipe = task {
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

                    let mutable nextDiscCheck = DateTimeOffset.UtcNow
                    let mutable nextFileCheck = DateTimeOffset.UtcNow

                    let mutable allSegmentsRead = false
                    
                    let cancellationToken = cancellationTokenSource.Token

                    while not disposing && not allSegmentsRead && not cancellationTokenSource.IsCancellationRequested do
                        let! len = input.ReadAsync(buffer, cancellationToken)
                        let streamFinished = len = 0

                        if streamFinished then
                            do! output.WriteAsync(zeroes, cancellationToken)
                        else
                            do! output.WriteAsync(buffer, 0, len, cancellationToken)

                        if nextDiscCheck <= DateTimeOffset.UtcNow then
                            nextDiscCheck <- nextDiscCheck + TimeSpan.FromSeconds(10.0)

                            let signalFile = tempFolder.GetPath(RealPath, DiscsOk)
                            if File.Exists(signalFile) then
                                let currentDriveInfo = CD.getDriveInfo AllDrives
                                if currentDriveInfo <> lastDriveInfo then
                                    File.Delete(signalFile)

                        if nextFileCheck <= DateTimeOffset.UtcNow then
                            nextFileCheck <- nextFileCheck + TimeSpan.FromSeconds(1.0)

                            let programInformationFilePath = tempFolder.GetPath(RealPath, CurrentIndex)
                            if File.Exists(programInformationFilePath) then
                                let! contents = File.ReadAllTextAsync(programInformationFilePath, cancellationToken)
                                inputPointer <- match contents.Trim() with Int32 n -> n | _ -> -1
                                File.Delete(programInformationFilePath)

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
                | :? IOException when disposing -> ()
                | ex -> printfn "%A" ex

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
                            printfn "[ShellSegmentSourceFactory] Cancelled"
                            return NoMoreSegments
                    }

                    member _.DisposeAsync() = new ValueTask(task {
                        printfn "[ShellSegmentSourceFactory] Disposing"

                        disposing <- true

                        cancellationTokenSource.Cancel()

                        do! pipe

                        do! generator.WaitForExitAsync()
                        generator.Dispose()

                        do! reencoder.WaitForExitAsync()
                        reencoder.Dispose()

                        tempFolder.Delete()
                    })
            }
        }
