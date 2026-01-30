namespace RadioHomeEngine

open System.Diagnostics
open System.Text.Json

module DiscDriveStatus =
    let private deserializeAs (_: 'T) (json: string) =
        JsonSerializer.Deserialize<'T>(json)

    let private getStatusAsync device = task {
        use proc =
            new ProcessStartInfo(
                "udevadm",
                $"info --json=short \"{device}\"",
                RedirectStandardOutput = true)
            |> Process.Start

        let! json = task {
            use sr = proc.StandardOutput
            return! sr.ReadToEndAsync()
        }

        let data = json |> deserializeAs {|
            ID_CDROM_MEDIA = Some ""
        |}

        return {|
            inserted = data.ID_CDROM_MEDIA = Some "1"
        |}
    }

    let mutable private current = Map.empty

    type DiscDriveEventArgs = {
        device: string
    }

    type DiscDriveStatusEventArgs = {
        device: string
        inserted: bool
    }

    let private statusChangedEvent = new Event<DiscDriveStatusEventArgs>()
    let private deviceRemovedEvent = new Event<DiscDriveEventArgs>()

    let statusChanged = statusChangedEvent.Publish
    let deviceRemoved = deviceRemovedEvent.Publish

    let refreshAllAsync () = task {
        let devices = DiscDrives.getAll ()
        let removed = Map.keys current |> Seq.except devices

        for device in devices do
            let! newStatus = getStatusAsync device
            if Map.tryFind device current <> Some newStatus then
                current <- current |> Map.add device newStatus
                statusChangedEvent.Trigger({
                    device = device
                    inserted = newStatus.inserted
                })

        for device in removed do
            current <- current |> Map.remove device
            deviceRemovedEvent.Trigger({
                device = device
            })
    }

    let removeAll () =
        for device in Map.keys current do
            current <- current |> Map.remove device
            deviceRemovedEvent.Trigger({
                device = device
            })
