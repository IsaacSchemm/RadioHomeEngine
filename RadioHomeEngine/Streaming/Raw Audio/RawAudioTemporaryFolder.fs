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

type RawAudioTemporaryFolder private (folderName: string) =
    let isWindows = Environment.OSVersion.Platform = PlatformID.Win32NT

    let unixTempPath = $"/tmp/{folderName}"

    let realTempPath =
        if isWindows
        then Path.Combine(Path.GetTempPath(), folderName)
        else unixTempPath

    let _ = Directory.CreateDirectory(realTempPath)

    member _.CreateFileSystemWatcher() =
        new FileSystemWatcher(
            realTempPath,
            "",
            EnableRaisingEvents = true)

    member _.GetPath(pathType: RawAudioTemporaryFolderPathType, filename: string) =
        match pathType with
        | UnixPath -> $"{unixTempPath}/{filename}"
        | RealPath -> Path.Combine(realTempPath, filename)

    member _.EnumerateSegments() =
        Directory.EnumerateFiles(realTempPath, "*.ts")

    static member Create() = new RawAudioTemporaryFolder($"RadioHomeEngine-{Guid.NewGuid()}")

    interface IDisposable with
        member this.Dispose() =
            if File.Exists(realTempPath) then File.Delete(realTempPath)
