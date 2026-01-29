namespace RadioHomeEngine

open System
open System.Diagnostics
open System.IO
open System.Runtime.Caching
open System.Threading
open System.Threading.Tasks

module DataCD =
    let extensions = set [
        //".aac"
        //".aif"
        //".aiff"
        //".flac"
        ".mp3"
        //".m4a"
        //".oga"
        //".ogg"
        //".wav"
        //".wma"
    ]

    type ITemporaryMount =
        inherit IAsyncDisposable
        inherit IDisposable

        abstract member Device: string with get
        abstract member MountPoint: string with get

    let establishTemporaryMountPointAsync (device: string) = task {
        let dir = Path.Combine(
            Path.GetTempPath(),
            $"{Guid.NewGuid()}")

        ignore (Directory.CreateDirectory(dir))

        use proc = Process.Start("mount", $"\"{device}\" \"{dir}\"")
        do! proc.WaitForExitAsync()

        let rec unmountAsync (attempts: int) = task {
            try
                use proc = Process.Start("umount", $"\"{dir}\"")
                do! proc.WaitForExitAsync()

                printfn "umount process quit with exit code %d" proc.ExitCode
                if proc.ExitCode <> 0 then
                    if attempts > 1 then
                        do! unmountAsync (attempts - 1)
            with ex ->
                Console.Error.WriteLine(ex)
        }

        return {
            new ITemporaryMount with
                member _.Device = device
                member _.MountPoint = dir
                member _.Dispose () = ignore (unmountAsync 3)
                member _.DisposeAsync () = ValueTask (unmountAsync 3)
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
        let cache = MemoryCache.Default
        let flag = new SemaphoreSlim(1, 1)

        let getKey device file =
            sprintf "FileCache:%s:%s:%d" device file.name file.size

        let getAsync device file = task {
            let key = getKey device file

            match cache.Get(key) with
            | :? (byte array) as data ->
                return data
            | _ ->
                do! flag.WaitAsync()

                try
                    use! mount = establishTemporaryMountPointAsync device

                    let path = Path.Combine(
                        mount.MountPoint,
                        file.name)

                    let! data = File.ReadAllBytesAsync(path)

                    let policy = new CacheItemPolicy(SlidingExpiration = TimeSpan.FromDays(1))
                    cache.Add(key, data, policy) |> ignore

                    return data
                finally
                    flag.Release() |> ignore
        }

    let storeAsync device file = task {
        let! data = FileCache.getAsync device file
        ignore data
    }

    let getStreamAsync device file = task {
        let! data = FileCache.getAsync device file
        return new MemoryStream(data, writable = false)
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
