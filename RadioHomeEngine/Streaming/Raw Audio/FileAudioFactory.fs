namespace RadioHomeEngine

open System.Diagnostics

type FileAudioFactory(path: string) =
    inherit FFmpegRawAudioFactory()

    let psi = new ProcessStartInfo()

    override _.InputParameters = [
        "-i"; path
    ]

    override _.ProgramInformation = {
        channelName = "File"
        title = path
        artists = []
        albums = []
    }
