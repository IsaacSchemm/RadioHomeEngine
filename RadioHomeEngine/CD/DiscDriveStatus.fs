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

        let get device =
            Map.tryFind device map

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

    let refreshAllAsync () = task {
        let devices = DiscDrives.getAll ()
        let removed = Map.keys current |> Seq.except devices

        for device in devices do
            let! newStatus = getStatusAsync device
            if Map.tryFind device current <> Some newStatus then
                current <- current |> Map.add device newStatus
                if newStatus.inserted then
                    do! MountPoints.mountAsync device
                else
                    do! MountPoints.unmountAsync device

        for device in removed do
            current <- current |> Map.remove device
            do! MountPoints.unmountAsync device
    }

    let getMountPoint device =
        MountPoints.get device

    let removeAllAsync () = task {
        for device in Map.keys current do
            current <- current |> Map.remove device
            do! MountPoints.unmountAsync device
    }
