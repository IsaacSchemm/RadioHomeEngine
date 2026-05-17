namespace RadioHomeEngine

open System
open System.Runtime.Caching

open LyrionCLI

module Speech =
    type Readable = {
        screen: string
        speech: Guid
    }

    let storeSpeech (text: string) =
        let guid = Guid.NewGuid()
        MemoryCache.Default.Set($"{guid}", text, DateTime.UtcNow.AddDays(1))
        guid

    let retrieveSpeech (guid: Guid) =
        match MemoryCache.Default[$"{guid}"] with
        | :? string as str -> str
        | _ -> ""

    let readAsync (player: Player) (readables: Readable seq) = task {
        let! address = Network.getAddressAsync ()

        match List.ofSeq readables with
        | [] ->
            do! Playlist.clearAsync player
        | r :: tail ->
            do! Playlist.playItemAsync player $"http://{address}:{Config.port}/Reader/Speech/{r.speech}" r.screen
            for r in tail do
                do! Playlist.addItemAsync player $"http://{address}:{Config.port}/Reader/Speech/{r.speech}" r.screen
    }
