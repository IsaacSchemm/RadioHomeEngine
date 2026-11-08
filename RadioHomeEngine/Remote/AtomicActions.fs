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
| PlayChannel of int * PlaylistPosition
| PlayPause
| Replay
| PlayCD of DiscDriveScope * PlaylistPosition
| RipCD of DiscDriveScope
| EjectCD of DiscDriveScope
| Stop

module AtomicActions =
    let tryGetAction (entry: string) = Seq.tryHead (seq {
        match entry with
        | "0" -> PlayCD (AllDrives, Now)
        | "00" -> Stop
        | Int32 n when n >= 0 -> PlayChannel (n, Now)
        | _ -> ()
    })

    let performActionAsync player atomicAction = task {
        match atomicAction with
        | PlayChannel (channelNumber, position) ->
            let channel =
                StreamRepository.getCurrentChannels ()
                |> Seq.where (fun c -> c.ChannelListing.ChannelNumber = channelNumber)
                |> Seq.tryHead
            match channel with
            | None -> ()
            | Some ch ->
                let channelName = ch.ChannelListing.ChannelInformation.ChannelName
                let! address = Network.getAddressAsync ()
                let url = $"http://{address}:{Config.port}/Stream/playlist-{channelNumber}.m3u8"
                let title = $"[{channelNumber}] {channelName}"
                match position with
                | Now -> do! Playlist.playItemAsync player url title
                | Last -> do! Playlist.addItemAsync player url title

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

        | Stop ->
            do! Players.simulateButtonAsync player "stop"
    }
