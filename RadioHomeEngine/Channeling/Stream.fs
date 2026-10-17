namespace RadioHomeEngine

open System

type StreamMode =
| Permanent
| Pausable
| Ephemeral

type StreamDefinition = {
    channelNumber: int
    factory: unit -> ISegmentSource
    mode: StreamMode
}

type Stream(definition: StreamDefinition) =
    let cache = new SegmentCache()
    let mutable buffer = None

    member _.Cache = cache
    member _.ChannelNumber = definition.channelNumber
    member _.Mode = definition.mode

    member _.StartAsync(cancellationToken) = task {
        match buffer with
        | Some _ -> ()
        | None ->
            printfn "[Stream] Starting new buffer for %d" definition.channelNumber
            buffer <- Some (new SegmentBuffer(definition.factory, cancellationToken))
    }

    member _.Read() =
        match buffer with
        | Some b -> cache.ReadFrom(b)
        | None -> ()

        cache.EvictStale()

    member _.StopAsync() = task {
        match buffer with
        | Some b ->
            printfn "[Stream] Stopping [%d]" definition.channelNumber
            buffer <- None
            do! (b :> IAsyncDisposable).DisposeAsync()
        | None -> ()
    }
