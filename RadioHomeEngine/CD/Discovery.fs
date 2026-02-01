namespace RadioHomeEngine

open System.Threading.Tasks
open FSharp.Control

module Discovery =
    let getDriveInfoForDeviceAsync device = task {
        printfn $"[Discovery] [{device}] Scanning drive {device}..."

        let! files = DataCD.scanDeviceAsync device

        let dataDisc = { files = files }

        let! audioDisc = task {
            if files <> [] then
                return None
            else
                return DiscDriveStatus.getTrackList device
        }

        return {
            device = device
            disc = {
                audio = [
                    match audioDisc with
                    | Some a when a.tracks <> [] -> a
                    | _ -> ()
                ]
                data = [
                    if dataDisc.files <> [] then dataDisc
                ]
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
