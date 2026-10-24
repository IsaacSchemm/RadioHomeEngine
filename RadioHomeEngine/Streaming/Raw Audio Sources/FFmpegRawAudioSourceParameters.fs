namespace RadioHomeEngine

open System.Threading.Tasks

type FFmpegRawAudioSourceParameters = {
    InputParameters: string
    ProgramInformationSource: IProgramInformationSource
}

module FFmpegRawAudioSourceParameters =
    let BrownNoise = {
        InputParameters = String.concat " " [
            "-f lavfi"
            $"-i \"anoisesrc=sample_rate=44100:color=brown\""
        ]
        ProgramInformationSource = {
            new IProgramInformationSource with
                member _.GetCurrentProgramInformationAsync(_) = Task.FromResult({
                    channelName = "Brown Noise"
                    title = "Brown Noise"
                    artists = []
                    albums = []
                })
        }
    }

    let Test1 = {
        InputParameters = String.concat " " [
            $"-i \"K:/Documents/Music/Steps - Gold Greatest Hits/05 Love’s Got a Hold on My Heart.flac\""
            "-ss 150"
        ]
        ProgramInformationSource = {
            new IProgramInformationSource with
                member _.GetCurrentProgramInformationAsync(_) = Task.FromResult({
                    channelName = "Test1"
                    title = "Love’s Got a Hold on My Heart"
                    artists = ["Steps"]
                    albums = ["Gold: Greatest Hits"]
                })
        }
    }
