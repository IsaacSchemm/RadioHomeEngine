namespace RadioHomeEngine

open System
open System.IO
open System.Threading.Channels
open System.Threading.Tasks

module LyrionCLI =
    let private channel = Channel.CreateUnbounded<string>()

    let private event = new Event<string list>()

    let sendCommandAsync command =
        command
        |> Seq.map Uri.EscapeDataString
        |> String.concat " "
        |> channel.Writer.WriteAsync

    let readNextCommandAsync cancellationToken =
        channel.Reader.ReadAsync(cancellationToken)

    let broadcastResponse response =
        event.Trigger(response)

    let subscribeToResponses callback =
        Observable.subscribe callback event.Publish

    let mutable initialConnectionEstablished = false

    exception NotConnectedException

    exception NoMatchingResponseException of command: string list

    let listenForAsync command chooser = task {
        let tcs = new TaskCompletionSource<'T>()

        use _ =
            event.Publish
            |> Observable.choose chooser
            |> Observable.subscribe tcs.SetResult

        while not initialConnectionEstablished do
            do! Task.Delay(TimeSpan.FromSeconds(1))

        let jointTask = Task.WhenAny [
            Task.Delay(TimeSpan.FromSeconds(5))
            tcs.Task
        ]

        let _ = task {
            try
                while not jointTask.IsCompleted do
                    do! sendCommandAsync command
                    do! Task.Delay(500)
            with ex -> Console.Error.WriteLine(ex)
        }

        let! completedTask = jointTask

        if completedTask <> tcs.Task then
            raise (NoMatchingResponseException command)

        return! tcs.Task
    }

    type Player = Player of string

    module General =
        let getMediaDirsAsync () = listenForAsync ["pref"; "mediadirs"; "?"] (fun command ->
            match command with
            | ["pref"; "mediadirs"; paths ] -> Some (Uri.UnescapeDataString(paths).Split(','))
            | _ -> None)

        let rescanAsync () = sendCommandAsync ["rescan"]

        let wipecacheAsync () = sendCommandAsync ["wipecache"]

        let exitAsync () = sendCommandAsync ["exit"]

        let restartServer () = sendCommandAsync ["restartserver"]

    module Players =
        let countAsync () = listenForAsync ["player"; "count"; "?"] (fun command ->
            match command with
            | ["player"; "count"; Int32 count] -> Some count
            | _ -> None)

        let getIdAsync index = listenForAsync ["player"; "id"; sprintf "%d" index; "?"] (fun command ->
            match command with
            | ["player"; "id"; Int32 i; id] when i = index -> Some (Player id)
            | _ -> None)

        let getNameAsync (Player id) = listenForAsync ["player"; "name"; id; "?"] (fun command ->
            match command with
            | "player" :: "name" :: x :: nameStrings when x = id -> Some (String.concat " " nameStrings)
            | _ -> None)

        let getPowerAsync (Player id) = listenForAsync [id; "power"; "?"] (fun command ->
            match command with
            | [x; "power"; "0"] when x = id -> Some false
            | [x; "power"; "1"] when x = id -> Some true
            | _ -> None)

        let setPowerAsync (Player id) state = sendCommandAsync [
            id
            "power"
            if state then "1" else "0"
        ]

        let togglePowerAsync (Player id) = sendCommandAsync [
            id
            "power"
        ]

        let getVolumeAsync (Player id) = listenForAsync [id; "mixer"; "volume"; "?"] (fun command ->
            match command with
            | [x; "mixer"; "volume"; Decimal value] when x = id -> Some value
            | _ -> None)

        let setVolumeAsync (Player id) volume = sendCommandAsync [
            id
            "mixer"
            "volume"
            volume
        ]

        let getMutingAsync (Player id) = listenForAsync [id; "mixer"; "muting"; "?"] (fun command ->
            match command with
            | [x; "mixer"; "muting"; "0"] when x = id -> Some false
            | [x; "mixer"; "muting"; "1"] when x = id -> Some true
            | _ -> None)

        let setMutingAsync (Player id) state = sendCommandAsync [
            id
            "mixer"
            "muting"
            if state then "1" else "0"
        ]

        let toggleMutingAsync (Player id) = sendCommandAsync [
            id
            "mixer"
            "muting"
        ]

        let getDisplayAsync (Player id) = listenForAsync [id; "display"; "?"; "?"] (fun command ->
            match command with
            | [x; "display"; line1; line2] when x = id -> Some (line1, line2)
            | _ -> None)

        let getDisplayNowAsync (Player id) = listenForAsync [id; "displaynow"; "?"; "?"] (fun command ->
            match command with
            | [x; "displaynow"; line1; line2] when x = id -> Some (line1, line2)
            | _ -> None)

        let setDisplayAsync (Player id) line1 line2 (duration: TimeSpan) = sendCommandAsync [
            id
            "display"
            line1
            line2
            $"{duration.TotalSeconds}"
        ]

        let simulateButtonAsync (Player id) buttoncode = sendCommandAsync [
            id
            "button"
            buttoncode
        ]

        let simulateIRAsync (Player id) (ircode: int) (time: decimal) = sendCommandAsync [
            id
            "ir"
            ircode.ToString("x8")
            time.ToString()
        ]

    module Playlist =
        let playAsync (Player id) = sendCommandAsync [
            id
            "play"
        ]

        let stopAsync (Player id) = sendCommandAsync [
            id
            "stop"
        ]

        let setPauseAsync (Player id) state = sendCommandAsync [
            id
            "pause"
            if state then "1" else "0"
        ]

        let setTimeAsync (Player id) origin (time: decimal) = sendCommandAsync [
            id
            "time"

            match origin with
            | SeekOrigin.Begin when time >= 0m ->
                sprintf "%f" time
            | SeekOrigin.Current ->
                sprintf "%+f" time
            | x ->
                failwithf "Unsupported seek origin %A %f" x time
        ]

        let togglePauseAsync (Player id) = sendCommandAsync [
            id
            "pause"
        ]

        type Mode = Playing | Stopped | Paused

        let getModeAsync (Player id) = listenForAsync [id; "mode"; "?"] (fun command ->
            match command with
            | [x; "mode"; "play"] when x = id -> Some Playing
            | [x; "mode"; "stop"] when x = id -> Some Stopped
            | [x; "mode"; "pause"] when x = id -> Some Paused
            | _ -> None)

        let getTitleAsync (Player id) = listenForAsync [id; "title"; "?"] (fun command ->
            match command with
            | [x; "title"; title] when x = id -> Some title
            | _ -> None)

        let playItemAsync (Player id) item title = sendCommandAsync [
            id
            "playlist"
            "play"
            item
            title
        ]

        let addItemAsync (Player id) item title = sendCommandAsync [
            id
            "playlist"
            "add"
            item
            title
        ]

        let clearAsync (Player id) = sendCommandAsync [
            id
            "playlist"
            "clear"
        ]

        let insertItemAsync (Player id) item title = sendCommandAsync [
            id
            "playlist"
            "insert"
            item
            title
        ]

        let getPathAsync (Player id) = listenForAsync [id; "path"; "?"] (fun command ->
            match command with
            | [x; "path"; path] when x = id -> Some path
            | _ -> None)
