namespace RadioHomeEngine

open System.Diagnostics
open System.Text.Json

module DiscDriveStatus =
    type EventArgs = {
        device: string
    }

    type StatusChangedEventArgs = {
        device: string
        inserted: bool
    }

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
            device = device
            inserted = data.ID_CDROM_MEDIA = Some "1"
        |}
    }

    let mutable private statuses = Map.empty

    let private statusChangedEvent = new Event<StatusChangedEventArgs>()
    let private deviceRemovedEvent = new Event<EventArgs>()

    let statusChanged = statusChangedEvent.Publish
    let deviceRemoved = deviceRemovedEvent.Publish

    let refreshAllAsync () = task {
        let devices = DiscDrives.getAll ()
        let removed = Map.keys statuses |> Seq.except devices

        for device in devices do
            let! current = getStatusAsync device
            if Map.tryFind device statuses <> Some current then
                statuses <- statuses |> Map.add device current
                statusChangedEvent.Trigger({
                    device = device
                    inserted = current.inserted
                })

        for device in removed do
            statuses <- statuses |> Map.remove device
            deviceRemovedEvent.Trigger({
                device = device
            })
    }

    let removeAll () =
        for device in Map.keys statuses do
            statuses <- statuses |> Map.remove device
            deviceRemovedEvent.Trigger({
                device = device
            })
