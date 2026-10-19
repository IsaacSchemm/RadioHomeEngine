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

    let mutable source = None
    let mutable sourceFinished = false
    let mutable flag = new SemaphoreSlim(1, 1)

    member _.Cache = cache
    member _.ChannelNumber = definition.channelNumber

    member _.StartAsync(cancellationToken: CancellationToken) = task {
        do! flag.WaitAsync(cancellationToken)
        try
            if source = None then
                printfn "[Stream] Starting new source for %d" definition.channelNumber
                let! src = definition.factory.CreateAsync()
                source <- Some src
        finally
            flag.Release() |> ignore
    }

    member _.ReadFromBufferAsync() = task {
        // TODO: have the stream object hold a background task which reads segments (or doesn't) from whatever stream StreamRepository.Service told it to

        match source with
        | None -> ()
        | Some source ->
            let keepRunning = cache.ReadFrom(source)
            sourceFinished <- not keepRunning

        cache.EvictStale()
    }

    member this.Interrupt(factory: ISegmentSourceFactory) =
        raise (new NotImplementedException())

    member this.ClearAllInterruptions() =
        raise (new NotImplementedException())

    member this.IsComplete =
        sourceFinished

    member this.StopAsync() = task {
        match source with
        | None -> ()
        | Some oldSource ->
            printfn "[Stream] Disposing of source [%d]" definition.channelNumber
            source <- None
            //ignore (oldSource.ReadAll())
            do! (oldSource :> IAsyncDisposable).DisposeAsync()
    }

    interface IAsyncDisposable with
        member this.DisposeAsync() = ValueTask (task {
            do! this.StopAsync()
        })
