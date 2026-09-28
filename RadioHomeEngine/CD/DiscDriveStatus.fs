namespace RadioHomeEngine

open System
open System.Diagnostics
open System.IO
open System.Threading
open System.Text.Json

/// Holds onto information about the discs inserted in disc drives on the system,
/// so this information doesn't need to be re-read from the disc every time.
module DiscDriveStatus =

    /// Maintains a set of mount points on the filesystem for data discs,
    /// mounting and unmounting them when requested.
    module private MountPoints =
        let mutable map = Map.empty
        let flag = new SemaphoreSlim(1, 1)

        let sharedTempPath = Path.Combine(
            Path.GetTempPath(),
            $"RadioHomeEngine/CD")

        let mountAsync device = task {
            do! flag.WaitAsync()

            try
                if Option.isNone (Map.tryFind device map) then
                    let path = Path.Combine(
                        sharedTempPath,
                        $"RadioHomeEngine/CD/{Guid.NewGuid()}")

                    ignore (Directory.CreateDirectory(path))

                    use proc = Process.Start("mount", $"-o ro \"{DiscDrive.getPath device}\" \"{path}\"")
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

                    // Uncomment this line if MP3s from the CD keep ending up in the library somehow
                    // do! LyrionCLI.General.wipecacheAsync ()
                | None -> ()

                map <- Map.remove device map
            finally
                ignore (flag.Release())
        }

    /// Maintains a set of track lists for currently inserted audio CDs,
    /// re-reading or clearing them when requested.
    module private TrackLists =
        let mutable map = Map.empty

        let scanAsync device = task {
            if Option.isNone (Map.tryFind device map) then
                let! info = AudioCD.getInfoForDeviceAsync device
                map <- Map.add device info map
        }

        let forgetAsync device = task {
            map <- Map.remove device map
        }

    let private deserializeAs (_: 'T) (json: string) =
        JsonSerializer.Deserialize<'T>(json)

    let private getStatusAsync device = task {
        use proc =
            new ProcessStartInfo(
                "udevadm",
                $"info --json=short \"{DiscDrive.getPath device}\"",
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

    /// Set up audio disc info and/or a filesystem mount point for a newly inserted disc.
    let attachAsync device = task {
        let! newStatus = getStatusAsync device

        let hasAudio = newStatus.audioTracks > 0
        let hasData = newStatus.dataTracks > 0

        if hasAudio then
            do! TrackLists.scanAsync device
        else
            do! TrackLists.forgetAsync device

        if hasData && not hasAudio then
            do! MountPoints.mountAsync device
        else
            do! MountPoints.unmountAsync device
    }

    /// Clear audio disc info and/or a filesystem mount point for a disc that has been, or is about to be, removed.
    let detachAsync device = task {
        do! TrackLists.forgetAsync device
        do! MountPoints.unmountAsync device
    }

    /// Set up audio disc info and/or a filesystem mount point for all inserted discs.
    let attachAllAsync () = task {
        let devices = DiscDrive.getAll ()

        for device in devices do
            do! attachAsync device

        let removed = Map.keys MountPoints.map |> Seq.except devices

        for device in removed do
            do! detachAsync device
    }

    /// Look for cached audio disc info, if any, for the disc in the given drive.
    let tryGetAudioDiscInfo device =
        Map.tryFind device TrackLists.map

    /// Look for an active mount point, if any, for the disc in the given drive.
    let tryGetMountPoint device =
        Map.tryFind device MountPoints.map

    /// Clear audio disc info and/or a filesystem mount point for all discs.
    let detachAllAsync () = task {
        for device in Map.keys MountPoints.map do
            do! detachAsync device
    }
