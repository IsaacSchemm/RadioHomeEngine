namespace RadioHomeEngine

open System
open System.Threading.Tasks
open FSharp.Control

/// Exposes an interface to get information about the contents of an audio CD,
/// combining multiple possible sources (MusicBrainz first, then CD-Text).
module AudioCD =
    let private asyncGetDiscIds device driveInfo = asyncSeq {
        let icedax_id = driveInfo.discid

        match icedax_id with
        | Some id -> yield id
        | None -> ()

        try
            let! abcde_id = Abcde.getMusicBrainzDiscIdAsync device |> Async.AwaitTask

            match abcde_id with
            | Some id -> yield id
            | _ -> ()
        with ex ->
            Console.Error.WriteLine(ex)
    }

    let private asyncQueryMusicBrainz discId = async {
        try
            printfn $"[AudioCD] Querying MusicBrainz for disc {discId}..."
            let! result = MusicBrainz.getInfoAsync discId |> Async.AwaitTask
            return result
        with ex ->
            Console.Error.WriteLine(ex)
            return None
    }

    /// Get info about the audio CD in the given disc drive.
    let getInfoForDeviceAsync device = task {
        printfn $"[AudioCD] [{DiscDrive.getPath device}] Scanning audio CD {DiscDrive.getPath device}..."

        let! icedax = Icedax.getInfoAsync device

        if icedax.disc.tracks = [] then
            printfn $"[AudioCD] [{DiscDrive.getPath device}] No tracks found on disc"
            return icedax.disc

        else
            printfn $"[AudioCD] [{DiscDrive.getPath device}] Preparing to query MusicBrainz..."

            let! candidate =
                asyncGetDiscIds device icedax.disc
                |> AsyncSeq.distinctUntilChanged
                |> AsyncSeq.mapAsync asyncQueryMusicBrainz
                |> AsyncSeq.choose id
                |> AsyncSeq.tryFirst

            match candidate with
            | Some newDisc ->
                printfn $"[AudioCD] [{DiscDrive.getPath device}] Using title {newDisc.titles} from MusicBrainz"
                return newDisc
            | None ->
                printfn $"[AudioCD] [{DiscDrive.getPath device}] Not found on MusicBrainz"
                printfn $"[AudioCD] [{DiscDrive.getPath device}] Using title {icedax.disc.titles} from icedax"
                return icedax.disc
    }

    /// Get info about the audio CDs in the given disc drives.
    let getInfoAsync scope = task {
        let! array =
            scope
            |> DiscDrive.getDevices
            |> Seq.map getInfoForDeviceAsync
            |> Task.WhenAll

        return Array.toList array
    }
