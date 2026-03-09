namespace RadioHomeEngine

open System
open System.IO
open System.Threading
open System.Threading.Tasks
open FSharp.Control

open LyrionCLI

type AtomicAction =
| PlaySiriusXMChannel of int
| Information
| PlayPause
| Replay
| PlayCD of DiscDriveScope
| RipCD of DiscDriveScope
| EjectCD of DiscDriveScope
| Forecast
| Stop

module AtomicActions =
    let zeroCodes = [
        ("00", Information, "Information")
        ("01", PlayCD AllDrives, "Play CD")
        ("02", RipCD AllDrives, "Rip CD")
        ("03", EjectCD AllDrives, "Eject CD")
        ("04", Forecast, "Weather")
        ("05", Stop, "Stop")
    ]

    let availablePrefixes =
        [0 .. 9]
        |> Seq.map (fun n -> $"{n:D2}")
        |> Seq.except [for (code, _, _) in zeroCodes do code]

    let getPrefixDetails () =
        PlayerConnections.GetAll()
        |> Seq.sortBy (fun cp -> cp.Name)
        |> Seq.zip availablePrefixes
        |> Seq.map (fun (code, cp) -> {|
            prefix = code
            player = cp.Player
            playerName = cp.Name
        |})

    let targetPlayerPrefix = "09"

    let rippingFlag = new SemaphoreSlim(1, 1)

    let beginRipAsync scope = ignore (task {
        do! rippingFlag.WaitAsync()

        try
            do! DataCD.ripAsync scope
            do! Abcde.ripAsync scope
        finally
            ignore (rippingFlag.Release())

        do! DiscDrives.ejectAsync scope
    })

    let tryGetAction (entry: string) = Seq.tryHead (seq {
        if entry.StartsWith("0") then
            for num, action, _ in zeroCodes do
                if num = entry then
                    action

        match entry with
        | Int32 n when n > 0 -> PlaySiriusXMChannel n
        | _ -> ()
    })

    let performActionAsync player atomicAction = task {
        match atomicAction with
        | PlaySiriusXMChannel channelNumber ->
            let! channels = SiriusXMClient.getChannelsAsync CancellationToken.None
            let name =
                channels
                |> Seq.where (fun c -> c.channelNumber = $"{channelNumber}")
                |> Seq.map (fun c -> c.name)
                |> Seq.tryHead

            match name with
            | None -> ()
            | Some channelName ->
                let! address = Network.getAddressAsync ()
                do! Playlist.playItemAsync player $"http://{address}:{Config.port}/SXM/PlayChannel?num={channelNumber}" $"[{channelNumber}] {channelName}"

        | Information ->
            let sec n = TimeSpan.FromSeconds(n)
            let wait n = Task.Delay(sec n)
            let title = "Numeric Entry"

            for code, _, name in zeroCodes do
                do! Players.setDisplayAsync player title $"{code}: {name}" (sec 10)
                do! wait 2

            for pd in getPrefixDetails () do
                do! Players.setDisplayAsync player title $"{pd.prefix}xx: {pd.playerName}" (sec 10)
                do! wait 3

            do! Players.setDisplayAsync player title "1-999: SiriusXM" (sec 10)
            do! wait 2

            match player with Player id ->
                do! Players.setDisplayAsync player "Player ID" $"{id}" (sec 10)
                do! wait 2

            let! ip = Network.getAddressAsync ()
            do! Players.setDisplayAsync player "Server" $"{ip}:{Config.port}" (sec 2)

        | PlayPause ->
            let! state = Playlist.getModeAsync player
            match state with
            | Playlist.Mode.Paused -> do! Playlist.setPauseAsync player false
            | Playlist.Mode.Playing -> do! Playlist.setPauseAsync player true
            | Playlist.Mode.Stopped -> do! Playlist.playAsync player

        | Replay ->
            do! Playlist.setTimeAsync player SeekOrigin.Current -10m

        | PlayCD scope ->
            do! Players.simulateButtonAsync player "stop"

            do! Playlist.clearAsync player

            let! address = Network.getAddressAsync ()

            let drives = Discovery.getDriveInfo scope

            for info in drives do
                match info.disc.audio with
                | None -> ()
                | Some audioDisc ->
                    for track in audioDisc.tracks do
                        let title =
                            match track.title with
                            | "" -> $"Track {track.position}"
                            | x -> x
                        do! Playlist.addItemAsync player $"http://{address}:{Config.port}/CD/PlayTrack?device={Uri.EscapeDataString(info.device)}&track={track.position}" title

                match info.disc.data with
                | None -> ()
                | Some dataDisc ->
                    for file in dataDisc.files do
                        match DataCD.tryGetPath info.device file with
                        | None -> ()
                        | Some path ->
                            do! Playlist.addItemAsync player $"file://{path}" file.name

            do! Playlist.playAsync player

        | RipCD scope ->
            beginRipAsync scope

        | EjectCD scope ->
            do! DiscDrives.ejectAsync scope

        | Forecast ->
            do! Players.setDisplayAsync player "Forecast" "Please wait..." (TimeSpan.FromSeconds(5))

            let! forecasts = Weather.getForecastsAsync CancellationToken.None
            let! alerts = Weather.getAlertsAsync CancellationToken.None

            do! Speech.readAsync player [
                for forecast in Seq.truncate 2 forecasts do
                    forecast
                for alert in alerts do
                    alert.info
            ]

        | Stop ->
            do! Players.simulateButtonAsync player "stop"
    }

    let performAlternateActionAsync player atomicAction = task {
        match atomicAction with
        | PlaySiriusXMChannel channelNumber ->
            do! Players.setDisplayAsync player "Info" "Please wait..." (TimeSpan.FromSeconds(10))

            let! channels = SiriusXMClient.getChannelsAsync CancellationToken.None
            let channel =
                channels
                |> Seq.where (fun c -> c.channelNumber = $"{channelNumber}")
                |> Seq.tryHead

            match channel with
            | None -> ()
            | Some c ->
                let! playlist = SiriusXMClient.getPlaylistAsync c.channelGuid c.channelId CancellationToken.None
                let song =
                    playlist.cuts
                    |> Seq.sortByDescending (fun cut -> cut.startTime)
                    |> Seq.tryHead

                match song with
                | None -> ()
                | Some c ->
                    let artist = String.concat " / " c.artists
                    do! Players.setDisplayAsync player artist c.title (TimeSpan.FromSeconds(10))

        | PlayCD scope ->
            do! Players.setDisplayAsync player "Info" "Please wait..." (TimeSpan.FromSeconds(10))

            let drives = Discovery.getDriveInfo scope

            let disc =
                drives
                |> Seq.map (fun drive -> drive.disc)
                |> Seq.choose (fun disc -> disc.audio)
                |> Seq.tryHead

            match disc with
            | None ->
                do! Players.setDisplayAsync player "CD" "No disc found" (TimeSpan.FromSeconds(10))
            | Some disc ->
                let title =
                    match disc.titles with
                    | [] -> "Unknown album"
                    | x -> String.concat ", " x
                let artist =
                    match disc.artists with
                    | [] -> "Unknown artist"
                    | x -> String.concat ", " x
                do! Players.setDisplayAsync player artist title (TimeSpan.FromSeconds(10))

        | _ -> ()
    }
