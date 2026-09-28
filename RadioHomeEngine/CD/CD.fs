namespace RadioHomeEngine

open System.Threading

/// Combines audio and data disc actions.
module CD =
    /// Gets both audio CD and data disc information for discs inserted in any disc drives within the scope.
    let getDriveInfo scope = [
        for device in DiscDrive.getDevices scope do {
            device = device
            disc = {
                audio = DiscDriveStatus.tryGetAudioDiscInfo device
                data = DataCD.tryGetDataDiscInfo device
            }
        }
    ]

    let private ripFlag = new SemaphoreSlim(1, 1)
    let mutable private currentScope = None

    let ripAsync scope = task {
        do! ripFlag.WaitAsync()

        try
            currentScope <- Some scope
            do! DataCD.ripAsync scope
            do! Abcde.ripAsync scope
        finally
            currentScope <- None
            ignore (ripFlag.Release())
    }

    let beginRip scope = ripAsync scope |> ignore

    let isCurrentlyRipping device =
        currentScope
        |> Option.map DiscDrive.getDevices
        |> Option.defaultValue []
        |> List.contains device
