namespace RadioHomeEngine

open System
open FSharp.Control

module Discovery =
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
            printfn $"[Discovery] Querying MusicBrainz for disc {discId}..."
            let! result = MusicBrainz.getInfoAsync discId |> Async.AwaitTask
            return result
        with ex ->
            Console.Error.WriteLine(ex)
            return None
    }

    let asyncGetDriveInfo device = async {
        printfn $"[Discovery] [{device}] Scanning drive {device}..."

        let! scanResults = Icedax.getInfoAsync device |> Async.AwaitTask

        let disc = scanResults.disc

        if disc.tracks = [] && scanResults.hasdata then
            let! files = DataCD.scanDeviceAsync device |> Async.AwaitTask

            return {
                device = device
                disc = DataDisc {
                    files = files
                }
            }

        else if disc.tracks = [] then
            printfn $"[Discovery] [{device}] No tracks found on disc"
            return {
                device = device
                disc = NoDisc
            }

        else
            printfn $"[Discovery] [{device}] Preparing to query MusicBrainz..."

            let! candidate =
                asyncGetDiscIds device disc
                |> AsyncSeq.distinctUntilChanged
                |> AsyncSeq.mapAsync asyncQueryMusicBrainz
                |> AsyncSeq.choose id
                |> AsyncSeq.tryFirst

            let disc =
                match candidate with
                | Some newDisc ->
                    printfn $"[Discovery] [{device}] Using title {newDisc.titles} from MusicBrainz"
                    newDisc
                | None ->
                    printfn $"[Discovery] [{device}] Not found on MusicBrainz"
                    printfn $"[Discovery] [{device}] Using title {disc.titles} from icedax"
                    scanResults.disc

            return {
                device = device
                disc =
                    if scanResults.hasdata
                    then HybridDisc disc
                    else AudioDisc disc
            }
    }

    [<Obsolete>]
    let asyncAutoMount device = async {
        return! asyncGetDriveInfo device
    }

    let getDriveInfoAsync scope = task {
        let! array =
            scope
            |> DiscDrives.getDevices
            |> Seq.map asyncGetDriveInfo
            |> Async.Parallel

        return Array.toList array
    }

    [<Obsolete>]
    let autoMountAsync scope = task {
        let! array =
            scope
            |> DiscDrives.getDevices
            |> Seq.map asyncAutoMount
            |> Async.Parallel

        return Array.toList array
    }
