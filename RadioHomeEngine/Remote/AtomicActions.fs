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

module AtomicActions =
    let zeroCodes = [
        ("00", Information, "Information")
        ("01", PlayCD AllDrives, "Play CD")
        ("02", RipCD AllDrives, "Rip CD")
        ("03", EjectCD AllDrives, "Eject CD")
        ("09", Forecast, "Weather")
    ]

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
        else
            match entry with
            | Int32 n -> PlaySiriusXMChannel n
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

            do! Players.setDisplayAsync player title "1-999: SiriusXM" (sec 10)
            do! wait 2

            match player with Player id ->
                do! Players.setDisplayAsync player "Player ID" $"{id}" (sec 10)
                do! wait 5

            let! ip = Network.getAddressAsync ()
            do! Players.setDisplayAsync player "Server" $"{ip}:{Config.port}" (sec 5)

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

            do! Players.setDisplayAsync player "Please wait" "Searching for tracks and files..." (TimeSpan.FromSeconds(999))

            let! drives = Discovery.getDriveInfoAsync scope

            do! Playlist.clearAsync player

            let! address = Network.getAddressAsync ()

            for info in drives do
                for track in info.disc.audio.tracks do
                    let title =
                        match track.title with
                        | "" -> $"Track {track.position}"
                        | x -> x
                    do! Playlist.addItemAsync player $"http://{address}:{Config.port}/CD/PlayTrack?device={Uri.EscapeDataString(info.device)}&track={track.position}" title

                for file in info.disc.data.files do
                    do! Players.setDisplayAsync player "Please wait" $"Reading file {file.name}..." (TimeSpan.FromSeconds(999))

                    let! path = DataCD.storeAsync info.device file
                    do! Playlist.addItemAsync player $"file://{path}" file.name

            do! Players.setDisplayAsync player "Please wait" "Finishing up..." (TimeSpan.FromMilliseconds(1))

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

            let! drives = Discovery.getDriveInfoAsync scope
            let disc =
                drives
                |> Seq.map (fun dr -> dr.disc)
                |> Seq.tryHead

            match disc with
            | None ->
                do! Players.setDisplayAsync player "CD" "No disc found" (TimeSpan.FromSeconds(10))
            | Some disc ->
                let title =
                    match disc.audio.titles with
                    | [] -> "Unknown album"
                    | x -> String.concat ", " x
                let artist =
                    match disc.audio.artists with
                    | [] -> "Unknown artist"
                    | x -> String.concat ", " x
                do! Players.setDisplayAsync player artist title (TimeSpan.FromSeconds(10))

        | _ -> ()
    }
