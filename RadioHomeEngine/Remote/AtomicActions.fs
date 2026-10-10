namespace RadioHomeEngine

open System
open System.IO
open System.Threading
open FSharp.Control

open LyrionCLI

type AtomicAction =
| ChangeChannel of int
| ViewCurrentChannel
| PlayCurrentChannel
| PlayBrownNoise
| PlayPause
| Replay
| PlayCD of DiscDriveScope
| RipCD of DiscDriveScope
| EjectCD of DiscDriveScope
| Forecast
| Stop

module AtomicActions =
    let tryGetActions (entry: string) = seq {
        match entry with
        | "000" -> Forecast
        | "00" -> PlayCD AllDrives
        | "0" -> Stop
        | Int32 n when n > 0 -> ChangeChannel n; PlayCurrentChannel
        | _ -> ()
    }

    let performActionAsync player atomicAction = task {
        match atomicAction with
        | ChangeChannel channelNumber ->
            do! TunerProxy.setCurrentChannelAsync channelNumber CancellationToken.None

        | ViewCurrentChannel ->
            do! Players.setDisplayAsync player "Info" "Please wait..." (TimeSpan.FromSeconds(10.0))

            let! channelName = TunerProxy.getCurrentChannelNameAsync CancellationToken.None

            let! playlist = TunerProxy.getCurrentChannelHistoryAsync CancellationToken.None
            let song =
                playlist
                |> Seq.sortByDescending (fun cut -> cut.startTime)
                |> Seq.tryHead

            match (channelName, song) with
            | Some ch, Some s ->
                let artist = String.concat " / " s.artists
                do! Players.setDisplayAsync player ch $"{artist} - {s.title}" (TimeSpan.FromSeconds(20.0))
            | _ ->
                do! Players.setDisplayAsync player "Info" "Please wait..." (TimeSpan.FromSeconds(0.1))

        | PlayCurrentChannel ->
            let! address = Network.getAddressAsync ()
            let url = $"http://{address}:{Config.port}/Proxy/playlist.m3u8"
            let title = $"{nameof(RadioHomeEngine)} ({address}:{Config.port})"
            do! Playlist.playItemAsync player url title

        | PlayBrownNoise ->
            let! address = Network.getAddressAsync ()
            do! Playlist.playItemAsync player $"http://{address}:{Config.port}/Noise/playlist.m3u8" "Noise"

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

            let! address = Network.getAddressAsync ()

            let drives = CD.getDriveInfo scope

            for info in drives do
                match info.disc.audio with
                | None -> ()
                | Some audioDisc ->
                    for track in audioDisc.tracks do
                        let title =
                            match track.title with
                            | "" -> $"Track {track.position}"
                            | x -> x
                        do! Playlist.addItemAsync player $"http://{address}:{Config.port}/CD/PlayTrack?id={Uri.EscapeDataString(DiscDrive.getId info.device)}&track={track.position}" title

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
            CD.beginRip scope

        | EjectCD scope ->
            do! DiscDrive.ejectAsync scope

        | Forecast ->
            do! Players.setDisplayAsync player "Forecast" "Please wait..." (TimeSpan.FromSeconds(5.0))

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
