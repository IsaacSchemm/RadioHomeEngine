namespace RadioHomeEngine

open System
open System.Diagnostics
open System.IO
open System.Threading.Tasks
open FSharp.Control
open System.Threading.Channels

type ShellSegmentSourceFactory(parameters: ShellWrapperParameters) =
    let hlsBitrate = 320 * 1024
    let segmentTimeSeconds = 10.0

    let quietParameters = "-nostats -hide_banner -loglevel warning"
    let formatParameters = "-f s16le -ar 44100 -ac 2"
    let hlsParameters = "-f hls -hls_time 10 -hls_segment_type mpegts -hls_flags program_date_time+temp_file -c:a aac -ac 2 -b:a 327680"

    let scriptFileName = "script.sh"
    let programInformationMarkerFileName = "currentIndex.txt"

    let mutable programInformation = {
        channelName = "Local Audio Source"
        title = "Nothing"
        artists = []
        albums = []
    }

    let zeroes = new ReadOnlyMemory<byte>(Array.zeroCreate<byte> 81920)

    let buildGeneratorScript (realTempPath: string) (unixTempPath: string) = [
        "#!/bin/sh"
        "set -e"

        "PATH=$PATH:$(pwd)"

        "while true; do"

        for (i, input) in Seq.indexed parameters.inputs do
            $"echo {i} > {unixTempPath}/{programInformationMarkerFileName}"

            match input with
            | AudioCD scope ->
                for info in CD.getDriveInfo scope do
                    match info.disc.audio with
                    | Some audioDisc when not (List.isEmpty audioDisc.tracks) ->
                        String.concat " " [
                            "icedax"
                            $"-D {DiscDrive.getPath info.device}"
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

            | AudioFile target ->
                let tempFileName = $"{Guid.NewGuid()}{Path.GetExtension(target)}"
                let tempFilePath = Path.Combine(realTempPath, tempFileName)
                ignore (File.CreateSymbolicLink(tempFilePath, target))

                String.concat " " [
                    "ffmpeg"
                    quietParameters
                    $"-i {unixTempPath}/{tempFileName}"
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

            | SpeechSynthesis text ->
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

        if not parameters.loop then
            "break"

        "done"
    ]

    interface IProgramInformationSource with
        member _.GetCurrentProgramInformationAsync(_) = Task.FromResult(programInformation)

    interface ISegmentSourceFactory with
        member this.ProgramInformationSource = this

        member this.CreateAsync() = task {
            let tempPathDirName = $"RadioHomeEngine-ShellWrapper-{Guid.NewGuid()}"

            let isWindows = Environment.OSVersion.Platform = PlatformID.Win32NT

            let bash =
                if Environment.OSVersion.Platform = PlatformID.Win32NT
                then "C:/Program Files/Git/bin/bash.exe"
                else "bash"

            let unixTempPath = $"/tmp/{tempPathDirName}"

            let realTempPath =
                if isWindows
                then Path.Combine(Path.GetTempPath(), tempPathDirName)
                else unixTempPath

            ignore (Directory.CreateDirectory(realTempPath))

            do! File.WriteAllLinesAsync(
                Path.Combine(realTempPath, scriptFileName),
                buildGeneratorScript realTempPath unixTempPath)

            let generator =
                new ProcessStartInfo(
                    bash,
                    $"{unixTempPath}/{scriptFileName}",
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true)
                |> Process.Start

            let realChunklistPath = Path.Combine(realTempPath, "chunklist.m3u8")

            let reencoder = Process.Start(new ProcessStartInfo(
                "ffmpeg",
                $"{quietParameters} -readrate 1 {formatParameters} -i - {hlsParameters} {realChunklistPath}",
                RedirectStandardInput = true))

            let channel = Channel.CreateBounded(3)
            let mutable disposing = false

            let pipe = task {
                try
                    let mutable mediaSequence = new UInt128(0UL, 500UL)

                    use control = generator.StandardInput
                    use input = generator.StandardOutput.BaseStream
                    use output = reencoder.StandardInput.BaseStream

                    let buffer = Array.zeroCreate<byte> 81920
                    let mutable nextFileCheck = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(1.0)

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

                            let markerPath = Path.Combine(realTempPath, programInformationMarkerFileName)
                            if File.Exists(markerPath) then
                                let! contents = File.ReadAllTextAsync(markerPath)

                                let index =
                                    match contents with
                                    | Int32 index -> index
                                    | _ -> -1

                                match List.tryItem index parameters.inputs with
                                | None ->
                                    programInformation <- { programInformation with title = $"{index}" }
                                | Some (AudioCD (SingleDrive drive)) ->
                                    let! info = AudioCD.getInfoForDeviceAsync drive
                                    programInformation <- {
                                        programInformation with
                                            title = "Audio CD"
                                            artists = info.artists
                                            albums = info.titles
                                    }
                                | Some (AudioCD _) ->
                                    programInformation <- { programInformation with title = "Audio CD" }
                                | Some (AudioFile path) ->
                                    programInformation <- { programInformation with title = Path.GetFileNameWithoutExtension(path) }
                                | Some (BrownNoise _) ->
                                    programInformation <- { programInformation with title = "Brown Noise" }
                                | Some (Silence _) ->
                                    programInformation <- { programInformation with title = "Silence" }
                                | Some (SpeechSynthesis text) ->
                                    programInformation <- { programInformation with title = text }

                                File.Delete(markerPath)

                            let mutable files = []

                            let infos = [
                                for file in Directory.EnumerateFiles(realTempPath, "*.ts") do
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

                channel.Writer.Complete()
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

                        disposing <- true

                        do! pipe

                        do! generator.WaitForExitAsync()
                        generator.Dispose()

                        do! reencoder.WaitForExitAsync()
                        reencoder.Dispose()

                        Directory.Delete(realTempPath, recursive = true)
                    })
            }
        }
