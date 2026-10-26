namespace RadioHomeEngine

open System
open System.Diagnostics
open System.IO
open System.Threading.Tasks

type ShellWrapper(parameters: ShellWrapperParameters) =
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

        let quietParameters = "-nostats -hide_banner -loglevel warning"
        let outputParameters = "-f s16le -ar 44100 -ac 2 -"

        let scriptFileName = "script.sh"
        let scriptFilePath = Path.Combine(realTempPath, scriptFileName)

        let currentIndexFileName = "currentIndex.txt"
        let currentIndexFilePath = Path.Combine(realTempPath, currentIndexFileName)

        let scriptLines = [
            "#!/bin/sh"
            "set -e"

            "PATH=$PATH:$(pwd)"

            "while true; do"

            for (i, input) in Seq.indexed parameters.inputs do
                $"echo {i} > {unixTempPath}/{currentIndexFileName}"

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
                                outputParameters
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
                        outputParameters
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

                        outputParameters
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

                        outputParameters
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
                            outputParameters
                        ]

            if not parameters.loop then
                "break"

            "done"
        ]

        do! File.WriteAllLinesAsync(scriptFilePath, scriptLines)

        let proc =
            new ProcessStartInfo(
                bash,
                $"{unixTempPath}/{scriptFileName}",
                //RedirectStandardInput = true,
                RedirectStandardOutput = true)
            |> Process.Start

        return {
            new IShellWrapperAudio with
                member _.Stream = proc.StandardOutput.BaseStream

                member _.GetCurrentProgramInformationAsync(cancellationToken) = task {
                    let! indexStr = File.ReadAllTextAsync(currentIndexFilePath, cancellationToken)

                    let index =
                        match indexStr.TrimEnd() with
                        | Int32 index -> index
                        | _ -> -1

                    let res =
                        match parameters.inputs |> List.tryItem index with
                        | None ->
                            {
                                channelName = $"{indexStr}"
                                title = $"{parameters.inputs}"
                                artists = []
                                albums = []
                            }
                        | Some (AudioCD AllDrives) ->
                            {
                                channelName = "Audio CD"
                                title = "Audio CD"
                                artists = []
                                albums = []
                            }
                        | Some (AudioCD (SingleDrive drive)) ->
                            match DiscDriveStatus.tryGetAudioDiscInfo drive with
                            | None ->
                                {
                                    channelName = "Audio CDs"
                                    title = "Audio CDs"
                                    artists = []
                                    albums = []
                                }
                            | Some audioDisc ->
                                {
                                    channelName = "Audio CD"
                                    title = Seq.tryHead audioDisc.titles |> Option.defaultValue "Unknown Album"
                                    artists = audioDisc.artists
                                    albums = []
                                }
                        | Some (AudioFile file) ->
                            {
                                channelName = "Audio File"
                                title = Path.GetFileNameWithoutExtension(file)
                                artists = []
                                albums = []
                            }
                        | Some (BrownNoise _) ->
                            {
                                channelName = "Brown Noise"
                                title = "Brown Noise"
                                artists = []
                                albums = []
                            }
                        | Some (Silence _) ->
                            {
                                channelName = "Silence"
                                title = "Silence"
                                artists = []
                                albums = []
                            }
                        | Some (SpeechSynthesis text) ->
                            {
                                channelName = "Message"
                                title = text
                                artists = []
                                albums = []
                            }

                    return res
                }

                member _.DisposeAsync() = new ValueTask(task {
                    try
                        proc.StandardOutput.BaseStream.Dispose()
                        do! proc.WaitForExitAsync()
                        proc.Dispose()
                        Directory.Delete(realTempPath, true)
                    with ex ->
                        Console.Error.WriteLine(ex)
                })
        }
    }
