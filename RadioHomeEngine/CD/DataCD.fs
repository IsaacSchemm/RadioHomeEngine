namespace RadioHomeEngine

open System
open System.Diagnostics
open System.IO
open System.Threading

module DataCD =
    let extensions = set [
        ".aac"
        ".aif"
        ".aiff"
        ".flac"
        ".mp3"
        ".m4a"
        ".oga"
        ".ogg"
        ".wav"
        ".wma"
    ]

    type ITemporaryMount =
        inherit IDisposable

        abstract member Device: string with get
        abstract member MountPoint: string with get

    let establishTemporaryMountPointAsync (device: string) = task {
        let dir = Path.Combine(
            Path.GetTempPath(),
            $"{Guid.NewGuid()}")

        ignore (Directory.CreateDirectory(dir))

        use proc = Process.Start("mount", $"-o ro \"{device}\" \"{dir}\"")
        do! proc.WaitForExitAsync()

        return {
            new ITemporaryMount with
                member _.Device = device
                member _.MountPoint = dir
                member _.Dispose () =
                    use proc = Process.Start("umount", $"\"{dir}\"")
                    proc.WaitForExit()

                    Directory.Delete(dir, recursive = false)
        }
    }

    let scanDeviceAsync device = task {
        try
            use! mount = establishTemporaryMountPointAsync device

            return [
                let dir = mount.MountPoint
                for file in Directory.EnumerateFiles(dir, "*.*", new EnumerationOptions(RecurseSubdirectories = true)) do
                    let fi = new FileInfo(file)

                    if Set.contains (fi.Extension.ToLowerInvariant()) extensions then
                        yield {
                            name = file.Substring(dir.Length).TrimStart(Path.DirectorySeparatorChar)
                            size = fi.Length
                        }
            ]
        with ex ->
            Console.Error.WriteLine(ex)
            return []
    }

    module FileCache =
        type TemporaryFile = {
            device: string
            fileInfo: DataDiscFileInfo
            path: string
        }

        let temporaryFiles = new ResizeArray<TemporaryFile>()
        let flag = new SemaphoreSlim(1, 1)

        let storeAsync device fileInfo = task {
            try
                do! flag.WaitAsync()

                let found =
                    temporaryFiles
                    |> Seq.where (fun f -> f.device = device && f.fileInfo = fileInfo)
                    |> Seq.tryHead

                match found with
                | Some knownFile ->
                    printfn "[DataCD] Using cached audio file %s" knownFile.path
                    return knownFile.path
                | _ ->
                    printfn "[DataCD] Caching audio file %s" fileInfo.name

                    use! mount = establishTemporaryMountPointAsync device

                    let extension = Path.GetExtension(fileInfo.name)

                    let copyFrom = Path.Combine(
                        Path.GetTempPath(),
                        $"S-{Guid.NewGuid()}{extension}")

                    let _ = File.CreateSymbolicLink(
                        copyFrom,
                        Path.Combine(
                            mount.MountPoint,
                            fileInfo.name))

                    let copyTo = Path.Combine(
                        Path.GetTempPath(),
                        $"T-{Guid.NewGuid()}{extension}")

                    use proc = Process.Start("cp", $"-L -v \"{copyFrom}\" \"{copyTo}\"")
                    do! proc.WaitForExitAsync()

                    File.Delete(copyFrom)

                    temporaryFiles.Add({ device = device; fileInfo = fileInfo; path = copyTo })

                    return copyTo
            finally
                flag.Release() |> ignore
        }

        let removeByDevice device =
            flag.Wait()

            try
                let invalidated =
                    temporaryFiles
                    |> Seq.where (fun f -> f.device = device)
                    |> Seq.toList

                for file in invalidated do
                    printfn "[DataCD] Deleting temporary file: %s" file.path
                    let _ = temporaryFiles.Remove(file)
                    File.Delete(file.path)
            finally
                flag.Release() |> ignore

        DiscDriveStatus.statusChanged |> Event.add (fun args -> if not args.inserted then removeByDevice args.device)
        DiscDriveStatus.deviceRemoved |> Event.add (fun args -> removeByDevice args.device)

    let storeAsync device file = task {
        return! FileCache.storeAsync device file
    }

    let ripAsync scope = task {
        let! dirs = LyrionCLI.General.getMediaDirsAsync()

        let mediaDir =
            dirs
            |> Seq.tryHead
            |> Option.defaultWith (fun () -> failwith "No media_dir found to rip to")

        for device in DiscDrives.getDevices scope do
            try
                use! mount = establishTemporaryMountPointAsync device
                let srcDir = mount.MountPoint

                let destDir = Path.Combine(
                    mediaDir,
                    $"""CD-ROM {DateTimeOffset.UtcNow.ToString("yyyy-MM-dd hh:mm:ss")}""")

                ignore (Directory.CreateDirectory(destDir))

                let proc = Process.Start("cp", $"""-r -v "{srcDir}/" "{destDir}/" """)
                do! proc.WaitForExitAsync()
            with ex ->
                Console.Error.WriteLine(ex)

        do! LyrionCLI.General.rescanAsync()
    }
