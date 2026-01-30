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
    audio: AudioDiscInfo list
    data: DataDiscInfo list
}

type DriveInfo = {
    device: string
    disc: DiscInfo
}
