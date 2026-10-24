namespace RadioHomeEngine

type BrownNoiseAudioFactory() =
    inherit FFmpegRawAudioFactory()

    override _.InputParameters = [
        "-f"; "lavfi"
        "-i"; "anoisesrc=sample_rate=44100:color=brown"
    ]

    override _.ProgramInformation = {
        channelName = "Brown Noise"
        title = "Brown Noise"
        artists = []
        albums = []
    }
