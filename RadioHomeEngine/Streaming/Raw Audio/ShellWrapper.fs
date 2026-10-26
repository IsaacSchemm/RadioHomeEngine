namespace RadioHomeEngine

open System
open System.Diagnostics
open System.IO
open System.Threading.Tasks

type ShellWrapperInput =
| AudioFile of path: string
| BrownNoise
| SpeechSynthesis of text: string

type ShellWrapperParameters = {
    inputs: ShellWrapperInput list
    loop: bool
}

type ShellWrapper(parameters: ShellWrapperParameters) =
    member this.CreateAsync() = task {
        let tempPathDirName = $"RadioHomeEngine-ShellWrapper-{Guid.NewGuid()}"

        let unixTempPath = $"/tmp/{tempPathDirName}"

        let realTempPath =
            if Environment.OSVersion.Platform = PlatformID.Win32NT
            then Path.Combine([|
                Path.GetTempPath()
                tempPathDirName
            |])
            else unixTempPath

        let ffmpeg =
            if Environment.OSVersion.Platform = PlatformID.Win32NT
            then "ffmpeg.exe"
            else "ffmpeg"

        ignore (Directory.CreateDirectory(realTempPath))

        let scriptLines = [
            "#!/bin/sh"
            "set -e"

            "PATH=$PATH:$(pwd)"

            "while true; do"

            for input in parameters.inputs do
                match input with
                | AudioFile target ->
                    let tempFileName = $"{Guid.NewGuid()}{Path.GetExtension(target)}"
                    let tempFilePath = Path.Combine(realTempPath, tempFileName)
                    ignore (File.CreateSymbolicLink(tempFilePath, target))
                    $"{ffmpeg} -i {unixTempPath}/{tempFileName} -ss 170 -f s16le -ar 44100 -ac 2 -"
                | BrownNoise ->
                    $"{ffmpeg} -nostats -hide_banner -loglevel warning -f lavfi -i anoisesrc=sample_rate=44100:color=brown -f s16le -ar 44100 -ac 2 -"
                | SpeechSynthesis text ->
                    if Environment.OSVersion.Platform <> PlatformID.Win32NT then
                        let textFileName = $"{Guid.NewGuid()}.txt"
                        File.WriteAllText(textFileName, text)
                        $"espeak -f {textFileName} --stdout -v us-mbrola-2 -s 140 -a 150 | {ffmpeg} -nostats -hide_banner -loglevel warning -f wav -i - -f s16le -ar 44100 -ac 2 -"

            $"{ffmpeg} -nostats -hide_banner -loglevel warning -f lavfi -i anullsrc=cl=stereo:sample_rate=44100 -t 2 -f s16le -ar 44100 -ac 2 -"

            if not parameters.loop then
                "break"

            "done"
        ]

        let scriptPath = Path.Combine(realTempPath, "script.sh")
        do! File.WriteAllLinesAsync(scriptPath, scriptLines)

        let bash =
            if Environment.OSVersion.Platform = PlatformID.Win32NT
            then "C:/Program Files/Git/bin/bash.exe"
            else "bash"

        let proc =
            new ProcessStartInfo(
                bash,
                $"{unixTempPath}/script.sh",
                //RedirectStandardInput = true,
                RedirectStandardOutput = true)
            |> Process.Start

        return {
            new IRawAudio with
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
