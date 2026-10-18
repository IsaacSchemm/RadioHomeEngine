namespace RadioHomeEngine

open System
open System.Threading
open System.Threading.Tasks

type StreamDefinition = {
    channelNumber: int
    factory: ISegmentSourceFactory
}

type Stream(definition: StreamDefinition) =
    let cache = new SegmentCache()

    let mutable buffer = None

    member _.Cache = cache
    member _.ChannelNumber = definition.channelNumber

    member _.StartAsync(cancellationToken) = task {
        if buffer = None then
            printfn "[Stream] Starting new buffer for %d" definition.channelNumber
            buffer <- Some (new SegmentBuffer(definition.factory, cancellationToken))
    }

    member _.ReadFromBufferAsync() = task {
        // TODO: can we use a LocalSegmentSource here directly and bypass SegmentBuffer? (This method shouldn't block if no new segments are available yet.)

        match buffer with
        | None -> ()
        | Some buffer -> cache.ReadFrom(buffer)

        cache.EvictStale()
    }

    member this.Interrupt(factory: ISegmentSourceFactory) =
        buffer |> Option.iter (fun buffer -> buffer.Interrupt(factory))

    [<Obsolete("Clear interruptions when stream becomes inactive instead")>]
    member this.IsInterrupted() =
        buffer |> Option.exists (fun buffer -> buffer.IsInterrupted())

    member this.ClearAllInterruptions() =
        buffer |> Option.iter (fun buffer -> buffer.ClearAllInterruptions())

    member this.IsComplete =
        buffer |> Option.forall (fun buffer -> buffer.IsComplete)

    member this.StopAsync() = task {
        match buffer with
        | None -> ()
        | Some oldBuffer ->
            buffer <- None
            do! (oldBuffer :> IAsyncDisposable).DisposeAsync()
    }

    interface IAsyncDisposable with
        member this.DisposeAsync() = ValueTask (task {
            do! this.StopAsync()
        })
