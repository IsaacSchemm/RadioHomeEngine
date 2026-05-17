namespace RadioHomeEngine

open System.Threading

module Ripping =
    let private flag = new SemaphoreSlim(1, 1)
    let mutable private currentScope = None

    let ripAsync scope = task {
        do! flag.WaitAsync()

        try
            currentScope <- Some scope
            do! DataCD.ripAsync scope
            do! Abcde.ripAsync scope
        finally
            currentScope <- None
            ignore (flag.Release())

        //do! DiscDrives.ejectAsync scope
    }

    let beginRip scope = ripAsync scope |> ignore

    let isCurrentlyRipping device =
        currentScope
        |> Option.map DiscDrives.getDevices
        |> Option.defaultValue []
        |> List.contains device
