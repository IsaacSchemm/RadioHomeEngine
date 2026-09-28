namespace RadioHomeEngine

/// A track on an audio CD.
type DiscTrackInfo = {
    title: string
    position: int
}

/// A file on a data CD.
type DiscFileInfo = {
    name: string
    size: int64
}

/// Information about an audio CD.
type AudioDiscInfo = {
    discid: string option
    titles: string list
    artists: string list
    tracks: DiscTrackInfo list
}

/// Information about a data CD.
type DataDiscInfo = {
    files: DiscFileInfo list
}

/// Information about a CD, which could be an audio, data, or mixed-mode CD, or none of the above.
type DiscInfo = {
    audio: AudioDiscInfo option
    data: DataDiscInfo option
} with
    member this.AudioDiscs = Option.toList this.audio
    member this.DataDiscs = Option.toList this.data

/// Information about a disc drive and the disc currently inserted in it (if any).
type DriveInfo = {
    device: DiscDrive
    disc: DiscInfo
}
