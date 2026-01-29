namespace RadioHomeEngine

open System.Diagnostics
open System.IO

module DiscDrives =
    let getAll () =
        seq { 0 .. 9 }
        |> Seq.map (fun n -> $"/dev/sr{n}")
        |> Seq.where File.Exists
        |> Seq.toList

    let exists device =
        getAll () |> List.contains device

    let getDevices scope =
        match scope with
        | SingleDrive x -> [if exists x then x]
        | AllDrives -> getAll ()

    let ejectDeviceAsync (device: string) = task {
        use proc = Process.Start("eject", $"-T {device}")
        do! proc.WaitForExitAsync()
    }

    let ejectAsync scope = task {
        for device in getDevices scope do
            do! ejectDeviceAsync device
    }
