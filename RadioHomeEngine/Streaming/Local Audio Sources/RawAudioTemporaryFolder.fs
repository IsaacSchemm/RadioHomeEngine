namespace RadioHomeEngine

open System
open System.Diagnostics
open System.IO
open System.Threading
open System.Threading.Tasks
open System.Threading.Channels
open FSharp.Control

type RawAudioTemporaryFolderPathType =
| RealPath
| UnixPath

type RawAudioTemporaryFolderFileType =
| Script
| CurrentIndex
| DiscsOk
| SpecificFile of string

type RawAudioTemporaryFolder private (folderName: string) =
    let isWindows = Environment.OSVersion.Platform = PlatformID.Win32NT

    let unixTempPath = $"/tmp/{folderName}"

    let realTempPath =
        if isWindows
        then Path.Combine(Path.GetTempPath(), folderName)
        else unixTempPath

    let _ = Directory.CreateDirectory(realTempPath)

    member _.GetPath(pathType: RawAudioTemporaryFolderPathType, fileType: RawAudioTemporaryFolderFileType) =
        let filename =
            match fileType with
            | Script -> "script.sh"
            | CurrentIndex -> "currentIndex.txt"
            | DiscsOk -> "discsok"
            | SpecificFile name -> name

        match pathType with
        | UnixPath -> $"{unixTempPath}/{filename}"
        | RealPath -> Path.Combine(realTempPath, filename)

    member _.EnumerateSegments() =
        Directory.EnumerateFiles(realTempPath, "*.ts")

    member _.Delete() = Directory.Delete(realTempPath, recursive = true)

    static member Create() = new RawAudioTemporaryFolder($"{Guid.NewGuid()}")
