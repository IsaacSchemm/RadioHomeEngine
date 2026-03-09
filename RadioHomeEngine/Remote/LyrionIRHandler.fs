namespace RadioHomeEngine

open System
open System.Globalization
open System.Threading.Tasks

open LyrionCLI
open LyrionIR

type LyrionIRHandler(player: Player) =
    let (|IRCode|_|) (str: string) =
        match Int32.TryParse(str, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture) with
        | true, value -> Some value
        | false, _ -> None

    let mutable promptText = None
    let mutable promptMonitor = Task.CompletedTask

    let writePromptAsync text = task {
        promptText <- Some text

        let promptHeader = "Enter channel or zero code"
        do! Players.setDisplayAsync player promptHeader text (TimeSpan.FromSeconds(10))

        if promptMonitor.IsCompleted then
            promptMonitor <- task {
                try
                    printf "Monitoring remote screen..."
                    let mutable finished = false
                    while not finished do
                        printf "."
                        do! Task.Delay(200)
                        let! (current, _) = Players.getDisplayNowAsync player
                        if current <> promptHeader then
                            printfn " screen reset."
                            finished <- true
                with ex ->
                    Console.Error.WriteLine(ex)

                promptText <- None
            }
    }

    let promptIndicator = "> "

    let appendToPromptAsync text =
        match promptText with
        | Some prefix -> writePromptAsync $"{prefix}{text}"
        | None -> writePromptAsync $"{promptIndicator}{text}"

    let clearAsync () = task {
        promptText <- None
        do! Players.setDisplayAsync player " " " " (TimeSpan.FromMilliseconds(1))
    }

    let mutable lastCode = 0
    let mutable lastTime = 0m

    let processIRAsync ircode time = task {
        let mapping =
            Mappings
            |> Map.tryFind ircode
            |> Option.defaultValue NoAction

        let debouncing =
            lastCode = ircode && abs (time - lastTime) < 0.2m

        lastCode <- ircode
        lastTime <- time

        match promptText, mapping with
        | None, IR name ->
            do! Players.simulateIRAsync player Slim[name] time

        | _, _ when debouncing -> ()

        | None, Number n ->
            if PlayerConnections.IsOn(player) then
                do! appendToPromptAsync $"{n}"

        | None, Button button ->
            do! Players.simulateButtonAsync player button

        | None, Atomic action ->
            if PlayerConnections.IsOn(player) then
                do! AtomicActions.performActionAsync player action

        | None, NoAction -> ()

        | Some _, Number n ->
            do! appendToPromptAsync $"{n}"

        | Some prompt, IR "favorites"
        | Some prompt, IR "arrow_left" when prompt.StartsWith(promptIndicator) && prompt <> promptIndicator ->
            do! writePromptAsync (prompt.Substring(0, prompt.Length - 1))

        | Some prompt, Button "knob_push" ->
            do! clearAsync ()

            let mutable entry = prompt.Substring(2)
            let mutable target = player

            while entry.StartsWith(AtomicActions.targetPlayerPrefix) do
                let index = int (entry.Substring(2, 2))
                match PlayerConnections.GetAll() |> List.tryItem index with
                | None -> ()
                | Some pc ->
                    target <- pc.Player
                    entry <- entry.Substring(4)

            match AtomicActions.tryGetAction entry with
            | None -> ()
            | Some action ->
                do! AtomicActions.performActionAsync target action

        | Some prompt, Atomic Information ->
            do! clearAsync ()

            let entry = prompt.Substring(2)

            match AtomicActions.tryGetAction entry with
            | None -> ()
            | Some action ->
                do! AtomicActions.performAlternateActionAsync player action

        | Some _, _ ->
            do! clearAsync()
    }

    let processCommandAsync command = task {
        try
            match command with
            | [x; "unknownir"; IRCode ircode; Decimal time] when Player x = player ->
                do! processIRAsync ircode time
            | _ -> ()
        with ex -> Console.Error.WriteLine(ex)
    }

    let subscriber = LyrionCLI.subscribeToResponses (processCommandAsync >> ignore)

    member _.Player = player

    interface IDisposable with
        member _.Dispose() =
            printfn "Stopping IR handler for %A" player
            subscriber.Dispose()
