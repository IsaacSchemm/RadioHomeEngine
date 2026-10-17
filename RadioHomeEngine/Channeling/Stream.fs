namespace RadioHomeEngine

open System
open System.Threading

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

    let mutable bufferStack = []
    let mutable lastToken = CancellationToken.None

    member _.Cache = cache
    member _.ChannelNumber = definition.channelNumber
    member _.Mode = definition.mode

    member _.StartAsync(cancellationToken) = task {
        match bufferStack with
        | _ :: _ -> ()
        | [] ->
            printfn "[Stream] Starting new buffer for %d" definition.channelNumber
            bufferStack <- new SegmentBuffer(definition.factory, cancellationToken) :: bufferStack
            lastToken <- cancellationToken
    }

    member _.PushAsync(factory: unit -> ISegmentSource) = task {
        bufferStack <- new SegmentBuffer(factory, lastToken) :: bufferStack
    }

    member _.PopAsync() = task {
        match bufferStack with
        | a :: b :: tail ->
            printfn "[Stream] Stopping top of stack [%d]" definition.channelNumber
            bufferStack <- b :: tail
            do! (a :> IAsyncDisposable).DisposeAsync()
            return true
        | _ ->
            return false
    }

    member _.Read() =
        match bufferStack with
        | buffer :: _ -> cache.ReadFrom(buffer)
        | [] -> ()

        cache.EvictStale()

    member _.StopAsync() = task {
        for buffer in bufferStack do
            printfn "[Stream] Stopping [%d]" definition.channelNumber
            bufferStack <- List.tail bufferStack
            do! (buffer :> IAsyncDisposable).DisposeAsync()
    }
