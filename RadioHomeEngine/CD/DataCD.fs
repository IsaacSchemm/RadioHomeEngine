namespace RadioHomeEngine

open System
open System.Diagnostics
open System.IO
open System.Runtime.Caching
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
        type ICachedFile =
            abstract member Path: string
            inherit IDisposable

        let cache = MemoryCache.Default
        let flag = new SemaphoreSlim(1, 1)

        let getKey device file =
            sprintf "FileCache:%s:%s:%d" device file.name file.size

        let storeAsTempFileAsync device file = task {
            let key = getKey device file

            match cache.Get(key) with
            | :? ICachedFile as cf ->
                return cf
            | _ ->
                do! flag.WaitAsync()

                try
                    use! mount = establishTemporaryMountPointAsync device

                    let sourceFile = Path.Combine(
                        mount.MountPoint,
                        file.name)

                    let extension = Path.GetExtension(file.name)

                    let tempFile = Path.Combine(
                        Path.GetTempPath(),
                        $"{Guid.NewGuid()}{extension}")

                    do! (task {
                        use fsIn = new FileStream(sourceFile, FileMode.Open, FileAccess.Read)
                        use fsOut = new FileStream(tempFile, FileMode.Create, FileAccess.Write)

                        do! fsIn.CopyToAsync(fsOut)
                    })

                    let cachedFile = {
                        new ICachedFile with
                            member _.Path = tempFile
                            member _.Dispose () =
                                if File.Exists(tempFile)
                                then File.Delete(tempFile)
                    }

                    let policy = new CacheItemPolicy(
                        SlidingExpiration = TimeSpan.FromDays(1),
                        RemovedCallback = fun args -> ())

                    cache.Add(key, cachedFile, policy) |> ignore

                    // TODO: clean up these files on application exit

                    return cachedFile
                finally
                    flag.Release() |> ignore
        }

    let storeAsync device file = task {
        let! file = FileCache.storeAsTempFileAsync device file
        return file.Path
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
                    $"CD-ROM {DateTimeOffset.UtcNow.ToUnixTimeSeconds()}")

                ignore (Directory.CreateDirectory(destDir))

                let proc = Process.Start("cp", $"""-r -v "{srcDir}/" "{destDir}/" """)
                do! proc.WaitForExitAsync()
            with ex ->
                Console.Error.WriteLine(ex)

        do! LyrionCLI.General.rescanAsync()
    }
