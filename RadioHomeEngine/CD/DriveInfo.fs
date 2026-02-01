namespace RadioHomeEngine

type DiscTrackInfo = {
    title: string
    position: int
}

type DiscFileInfo = {
    name: string
    size: int64
}

type AudioDiscInfo = {
    discid: string option
    titles: string list
    artists: string list
    tracks: DiscTrackInfo list
}

type DataDiscInfo = {
    files: DiscFileInfo list
}

type DiscInfo = {
    audio: AudioDiscInfo option
    data: DataDiscInfo option
} with
    member this.AudioDiscs = Option.toList this.audio
    member this.DataDiscs = Option.toList this.data

type DriveInfo = {
    device: string
    disc: DiscInfo
}
