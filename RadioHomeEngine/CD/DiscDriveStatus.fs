namespace RadioHomeEngine

open System
open System.Diagnostics
open System.IO
open System.Threading
open System.Text.Json

module DiscDriveStatus =
    module private MountPoints =
        let mutable map = Map.empty
        let flag = new SemaphoreSlim(1, 1)

        let mountAsync device = task {
            do! flag.WaitAsync()

            try
                if Option.isNone ( Map.tryFind device map) then
                    let path = Path.Combine(
                        Path.GetTempPath(),
                        $"{Guid.NewGuid()}")

                    ignore (Directory.CreateDirectory(path))

                    use proc = Process.Start("mount", $"-o ro \"{device}\" \"{path}\"")
                    do! proc.WaitForExitAsync()

                    if proc.ExitCode <> 0 then
                        failwithf "mount quit with exit code %d" proc.ExitCode

                    map <- Map.add device path map
            finally
                ignore (flag.Release())
        }

        let unmountAsync device = task {
            do! flag.WaitAsync()

            try
                match Map.tryFind device map with
                | Some path ->
                    use proc = Process.Start("umount", $"-l \"{path}\"")
                    do! proc.WaitForExitAsync()

                    Directory.Delete(path, recursive = false)
                | None -> ()

                map <- Map.remove device map
            finally
                ignore (flag.Release())
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
            ID_CDROM_MEDIA_TRACK_COUNT_AUDIO = Some ""
            ID_CDROM_MEDIA_TRACK_COUNT_DATA = Some ""
        |}

        return {|
            inserted = data.ID_CDROM_MEDIA = Some "1"
            audioTracks =
                match data.ID_CDROM_MEDIA_TRACK_COUNT_AUDIO with
                | Some (Int32 i) -> i
                | _ -> 0
            dataTracks =
                match data.ID_CDROM_MEDIA_TRACK_COUNT_DATA with
                | Some (Int32 i) -> i
                | _ -> 0
        |}
    }

    let mountAllAsync () = task {
        let devices = DiscDrives.getAll ()

        for device in devices do
            let! newStatus = getStatusAsync device

            if newStatus.inserted && newStatus.audioTracks = 0 && newStatus.dataTracks > 0 then
                do! MountPoints.mountAsync device
            else
                do! MountPoints.unmountAsync device

        let removed = Map.keys MountPoints.map |> Seq.except devices

        for device in removed do
            do! MountPoints.unmountAsync device
    }

    let getMountPoint device =
        Map.tryFind device MountPoints.map

    let unmountAllAsync () = task {
        for device in Map.keys MountPoints.map do
            do! MountPoints.unmountAsync device
    }
