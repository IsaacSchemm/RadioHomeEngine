namespace RadioHomeEngine

open System
open System.Threading.Tasks
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

    let getDriveInfoForDeviceAsync device = task {
        printfn $"[Discovery] [{device}] Scanning drive {device}..."

        let! icedax = Icedax.getInfoAsync device

        let! audioDisc = task {
            if icedax.disc.tracks = [] then
                printfn $"[Discovery] [{device}] No tracks found on disc"
                return icedax.disc

            else
                printfn $"[Discovery] [{device}] Preparing to query MusicBrainz..."

                let! candidate =
                    asyncGetDiscIds device icedax.disc
                    |> AsyncSeq.distinctUntilChanged
                    |> AsyncSeq.mapAsync asyncQueryMusicBrainz
                    |> AsyncSeq.choose id
                    |> AsyncSeq.tryFirst

                match candidate with
                | Some newDisc ->
                    printfn $"[Discovery] [{device}] Using title {newDisc.titles} from MusicBrainz"
                    return newDisc
                | None ->
                    printfn $"[Discovery] [{device}] Not found on MusicBrainz"
                    printfn $"[Discovery] [{device}] Using title {icedax.disc.titles} from icedax"
                    return icedax.disc
        }

        let! files =
            if icedax.hasdata
            then DataCD.scanDeviceAsync device
            else Task.FromResult([])

        return {
            device = device
            disc = {
                audio = audioDisc
                data = { files = files }
            }
        }
    }

    let getDriveInfoAsync scope = task {
        let! array =
            scope
            |> DiscDrives.getDevices
            |> Seq.map getDriveInfoForDeviceAsync
            |> Task.WhenAll

        return Array.toList array
    }
