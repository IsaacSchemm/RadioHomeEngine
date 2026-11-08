namespace RadioHomeEngine

open System
open System.Globalization
open System.Text.RegularExpressions
open System.Threading
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

        let promptHeader = "Enter SiriusXM channel"
        do! Players.setDisplayAsync player promptHeader text (TimeSpan.FromSeconds(10.0))

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
        do! Players.setDisplayAsync player " " " " (TimeSpan.FromMilliseconds(1.0))
    }

    let mutable lastCode = 0
    let mutable lastTime = 0m

    let tryGetChannelNumberAsync player = task {
        let! url = LyrionCLI.Playlist.getPathAsync player
        let! address = Network.getAddressAsync ()
        match Uri.TryCreate(url, UriKind.Absolute) with
        | (true, uri) when uri.Host = address && uri.Port = Config.port ->
            let m = Regex.Match(uri.AbsolutePath, "/Stream/playlist-([0-9]+).m3u8")
            if m.Success then
                return Some (int m.Groups[1].Value)
            else
                return None
        | _ ->
            return None
    }

    let showProgramInformationForChannelAsync channelNumber = task {
        do! Players.setDisplayAsync player "Info" "Please wait..." (TimeSpan.FromSeconds(10.0))

        let! info = StreamRepository.getChannelListingAndProgramInformationAsync channelNumber CancellationToken.None
        let listing = info.channelListing
        let program = info.programInformation

        do! Players.setDisplayAsync player $"[{listing.ChannelNumber}] {program.ChannelName}" program.ProgramName (TimeSpan.FromSeconds(20.0))
    }

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

            let entry = prompt.Substring(2)

            match AtomicActions.tryGetAction entry with
            | None -> ()
            | Some action ->
                do! AtomicActions.performActionAsync player action

        | Some prompt, Info ->
            do! clearAsync ()

            let entry = prompt.Substring(2)

            match entry with
            | Int32 i -> do! showProgramInformationForChannelAsync i
            | _ -> ()

        | None, Info ->
            let! channelNumber = tryGetChannelNumberAsync player
            match channelNumber with
            | Some num -> do! showProgramInformationForChannelAsync  num
            | None -> ()

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
