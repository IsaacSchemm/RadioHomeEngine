namespace RadioHomeEngine

open System
open System.Diagnostics
open System.IO

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

    let tryGetDataDiscInfo device =
        DiscDriveStatus.tryGetMountPoint device
        |> Option.map (fun dir -> {
            files = [
                for file in Directory.EnumerateFiles(dir, "*.*", new EnumerationOptions(RecurseSubdirectories = true)) do
                    let fi = new FileInfo(file)

                    if Set.contains (fi.Extension.ToLowerInvariant()) extensions then
                        yield {
                            name = file.Substring(dir.Length).TrimStart(Path.DirectorySeparatorChar)
                            size = fi.Length
                        }
            ]
        })

    let tryGetPath device file =
        DiscDriveStatus.tryGetMountPoint device
        |> Option.map (fun dir -> Path.Combine(dir, file.name))

    let ripAsync scope = task {
        let! dirs = LyrionCLI.General.getMediaDirsAsync()

        let mediaDir =
            dirs
            |> Seq.tryHead
            |> Option.defaultWith (fun () -> failwith "No media_dir found to rip to")

        let destDir = Path.Combine(
            mediaDir,
            "CD-ROM")

        for device in DiscDrives.getDevices scope do
            try
                match DiscDriveStatus.tryGetMountPoint device with
                | Some srcDir ->
                    ignore (Directory.CreateDirectory(destDir))

                    let proc = Process.Start("cp", $"""-r -v "{srcDir}/" "{destDir}/" """)
                    do! proc.WaitForExitAsync()
                | None -> ()
            with ex ->
                Console.Error.WriteLine(ex)

        do! LyrionCLI.General.rescanAsync()
    }
