namespace RadioHomeEngine

open System
open System.Diagnostics
open System.IO
open System.Threading.Tasks

/// An audio source that provides a stream of CD-quality audio samples.
type IShellWrapperAudio =
    inherit IAsyncDisposable
    abstract member Stream: Stream

type ShellWrapperDuration =
| Finite of TimeSpan
| Infinite

type ShellWrapperInput =
| AudioCD of DiscDriveScope
| AudioFile of path: string
| BrownNoise of duration: ShellWrapperDuration
| Silence of duration: ShellWrapperDuration
| SpeechSynthesis of text: string

type ShellWrapperParameters = {
    inputs: ShellWrapperInput list
    loop: bool
}

type ShellWrapper(parameters: ShellWrapperParameters) =
    member this.CreateAsync() = task {
        let tempPathDirName = $"RadioHomeEngine-ShellWrapper-{Guid.NewGuid()}"

        let isWindows = Environment.OSVersion.Platform = PlatformID.Win32NT

        let bash =
            if Environment.OSVersion.Platform = PlatformID.Win32NT
            then "C:/Program Files/Git/bin/bash.exe"
            else "bash"

        let ffmpeg =
            if Environment.OSVersion.Platform = PlatformID.Win32NT
            then "ffmpeg.exe"
            else "ffmpeg"

        let unixTempPath = $"/tmp/{tempPathDirName}"

        let realTempPath =
            if isWindows
            then Path.Combine(Path.GetTempPath(), tempPathDirName)
            else unixTempPath

        ignore (Directory.CreateDirectory(realTempPath))

        let quietParameters = "-nostats -hide_banner -loglevel warning"
        let outputParameters = "-f s16le -ar 44100 -ac 2 -"

        let scriptLines = [
            "#!/bin/sh"
            "set -e"

            "PATH=$PATH:$(pwd)"

            "while true; do"

            for input in parameters.inputs do
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
                                ffmpeg
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
                        ffmpeg
                        quietParameters
                        $"-i {unixTempPath}/{tempFileName}"
                        outputParameters
                    ]

                | BrownNoise duration ->
                    String.concat " " [
                        ffmpeg
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
                        ffmpeg
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
                            ffmpeg
                            quietParameters
                            "-f wav"
                            "-i -"
                            outputParameters
                        ]

            if not parameters.loop then
                "break"

            "done"
        ]

        let scriptPath = Path.Combine(realTempPath, "script.sh")
        do! File.WriteAllLinesAsync(scriptPath, scriptLines)

        let proc =
            new ProcessStartInfo(
                bash,
                $"{unixTempPath}/script.sh",
                //RedirectStandardInput = true,
                RedirectStandardOutput = true)
            |> Process.Start

        return {
            new IShellWrapperAudio with
                member _.Stream = proc.StandardOutput.BaseStream
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

    member this.ProgramInformation = {
        channelName = "ShellWrapperRawAudioFactory"
        title = "ShellWrapperRawAudioFactory"
        artists = []
        albums = []
    }
