namespace RadioHomeEngine

open System
open System.IO
open System.Threading
open FSharp.Control

open LyrionCLI

type PlaylistPosition =
| Now
| Last

type AtomicAction =
| PlayCurrentChannel of PlaylistPosition
| PlaySiriusXMChannel of int * PlaylistPosition
| PlayBrownNoise
| PlayPause
| Replay
| PlayCD of DiscDriveScope * PlaylistPosition
| RipCD of DiscDriveScope
| EjectCD of DiscDriveScope
| Forecast
| Stop

module AtomicActions =
    let tryGetAction (entry: string) = Seq.tryHead (seq {
        match entry with
        | "000" -> Forecast
        | "00" -> PlayCD (AllDrives, Now)
        | "0" -> Stop
        | Int32 n when n > 0 -> PlaySiriusXMChannel (n, Now)
        | _ -> ()
    })

    let performActionAsync player atomicAction = task {
        match atomicAction with
        | PlayCurrentChannel position ->
            let! address = Network.getAddressAsync ()
            let url = $"http://{address}:{Config.port}/Proxy/playlist.m3u8"
            let title = "SiriusXM"

            match position with
            | Now -> do! Playlist.playItemAsync player url title
            | Last -> do! Playlist.addItemAsync player url title

        | PlaySiriusXMChannel (channelNumber, position) ->
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
                let url = $"http://{address}:{Config.port}/SXM/PlayChannel?num={channelNumber}"
                let title = $"[{channelNumber}] {channelName}"
                match position with
                | Now -> do! Playlist.playItemAsync player url title
                | Last -> do! Playlist.addItemAsync player url title

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

        | PlayCD (scope, position) ->
            do! Players.simulateButtonAsync player "stop"

            if position = Now then
                do! Playlist.clearAsync player

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

    let performAlternateActionAsync player atomicAction = task {
        match atomicAction with
        | PlayCurrentChannel _ ->
            do! Players.setDisplayAsync player "Info" "Please wait..." (TimeSpan.FromSeconds(10.0))

            let! playlist = TunerProxy.getCurrentChannelHistoryAsync CancellationToken.None
            let song =
                playlist
                |> Seq.sortByDescending (fun cut -> cut.startTime)
                |> Seq.tryHead

            match song with
            | None -> ()
            | Some c ->
                let artist = String.concat " / " c.artists
                do! Players.setDisplayAsync player artist c.title (TimeSpan.FromSeconds(10.0))

        | PlaySiriusXMChannel (channelNumber, _) ->
            do! Players.setDisplayAsync player "Info" "Please wait..." (TimeSpan.FromSeconds(10.0))

            let! playlist = SiriusXMClient.tryGetPlaylistAsync channelNumber CancellationToken.None
            let song =
                playlist
                |> Option.map (fun p -> p.cuts)
                |> Option.defaultValue []
                |> Seq.sortByDescending (fun cut -> cut.startTime)
                |> Seq.tryHead

            match song with
            | None -> ()
            | Some c ->
                let artist = String.concat " / " c.artists
                do! Players.setDisplayAsync player artist c.title (TimeSpan.FromSeconds(10.0))

        | PlayCD (scope, _) ->
            do! Players.setDisplayAsync player "Info" "Please wait..." (TimeSpan.FromSeconds(10.0))

            let drives = CD.getDriveInfo scope

            let disc =
                drives
                |> Seq.map (fun drive -> drive.disc)
                |> Seq.choose (fun disc -> disc.audio)
                |> Seq.tryHead

            match disc with
            | None ->
                do! Players.setDisplayAsync player "CD" "No disc found" (TimeSpan.FromSeconds(10.0))
            | Some disc ->
                let title =
                    match disc.titles with
                    | [] -> "Unknown album"
                    | x -> String.concat ", " x
                let artist =
                    match disc.artists with
                    | [] -> "Unknown artist"
                    | x -> String.concat ", " x
                do! Players.setDisplayAsync player artist title (TimeSpan.FromSeconds(10.0))

        | _ -> ()
    }
