namespace RadioHomeEngine

type TrackInfo = {
    title: string
    position: int
}

// TODO: unify audio and data disc types

type AudioDiscInfo = {
    discid: string option
    titles: string list
    artists: string list
    tracks: TrackInfo list
}

type DataDiscFileInfo = {
    name: string
    size: int64
}

type DataDiscInfo = {
    files: DataDiscFileInfo list
}

type DiscInfo =
| AudioDisc of AudioDiscInfo
| HybridDisc of AudioDiscInfo
| DataDisc of DataDiscInfo
| NoDisc

type DriveInfo = {
    device: string
    disc: DiscInfo
} with
    member this.AudioDiscs = [match this.disc with AudioDisc x -> x | HybridDisc x -> x | _ -> ()]
    member this.DataDiscs = [match this.disc with DataDisc x -> x | _ -> ()]
    member this.HasData =
        match this.disc with
        | HybridDisc _
        | DataDisc _ -> true
        | _ -> false
