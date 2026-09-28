namespace RadioHomeEngine

open System
open System.Diagnostics
open System.IO
open System.Text

/// A disc drive on the system, identified by its device path (e.g. /dev/sr0).
type DiscDrive = DiscDrive of string

/// The scope of an operation that uses or affects one or more disc drives.
type DiscDriveScope = SingleDrive of DiscDrive | AllDrives

module DiscDrive =
    /// Gets the device path of a disc drive (e.g. /dev/sr0).
    let getPath dd =
        match dd with DiscDrive x -> x

    /// Gets a unique ID for the disc drive (derived from its path) which can be used in URL parameters and API calls.
    let getId dd =
        dd
        |> getPath
        |> Encoding.UTF8.GetBytes
        |> Convert.ToHexString

    /// Finds a disc drive from its unique ID (from a URL parameter or API call)
    let fromId (id: string) =
        id
        |> Convert.FromHexString
        |> Encoding.UTF8.GetString
        |> DiscDrive

    /// Lists all disc drives on the system.
    let getAll () =
        seq { 0 .. 9 }
        |> Seq.map (fun n -> $"/dev/sr{n}")
        |> Seq.where File.Exists
        |> Seq.map DiscDrive
        |> Seq.toList

    /// Determines whether a given disc drive (still) exists on the system.
    let exists device =
        getAll () |> List.contains device

    /// Lists all disc drives on the system that are within the given scope.
    let getDevices scope =
        match scope with
        | SingleDrive x -> [if exists x then x]
        | AllDrives -> getAll ()

    /// Ejects a disc drive.
    let ejectDeviceAsync (device: DiscDrive) = task {
        use proc = Process.Start("eject", $"-T {getPath device}")
        do! proc.WaitForExitAsync()
    }

    /// Ejects all disc drives that are within the given scope.
    let ejectAsync scope = task {
        for device in getDevices scope do
            do! ejectDeviceAsync device
    }
