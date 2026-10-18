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

    let mutable bufferStack = []
    let mutable lastToken = CancellationToken.None

    member _.Cache = cache
    member _.ChannelNumber = definition.channelNumber

    member _.StartAsync(cancellationToken) = task {
        if bufferStack = [] then
            printfn "[Stream] Starting new buffer for %d" definition.channelNumber

            bufferStack <- new SegmentBuffer(definition.factory, cancellationToken) :: bufferStack
            lastToken <- cancellationToken
    }

    member _.PushAsync(factory: ISegmentSourceFactory) = task {
        if bufferStack = [] then
            raise (new NotImplementedException())

        printfn "[Stream] Adding to stack [%d]: %A" definition.channelNumber factory
        bufferStack <- new SegmentBuffer(factory, lastToken) :: bufferStack
    }

    member _.PopAsync() = task {
        match bufferStack with
        | a :: b :: tail ->
            printfn "[Stream] Removing from stack [%d]" definition.channelNumber
            bufferStack <- b :: tail
            do! (a :> IAsyncDisposable).DisposeAsync()
            return true
        | _ ->
            return false
    }

    member _.Read() =
        match bufferStack with
        | buffer :: nextBuffer :: tail ->
            cache.ReadFrom(buffer)

            if buffer.IsComplete then
                printfn "[Stream] Removing completed buffer from stack [%d]" definition.channelNumber
                bufferStack <- nextBuffer :: tail
        | [buffer] ->
            cache.ReadFrom(buffer)
        | [] -> ()

        cache.EvictStale()

    member this.IsComplete =
        bufferStack |> List.forall (fun buffer -> buffer.IsComplete)

    member this.StopAsync() = task {
        for buffer in bufferStack do
            printfn "[Stream] Stopping [%d]; disposing buffer %A" definition.channelNumber buffer
            bufferStack <- List.tail bufferStack
            do! (buffer :> IAsyncDisposable).DisposeAsync()
    }

    interface IAsyncDisposable with
        member this.DisposeAsync() = ValueTask (task {
            do! this.StopAsync()
        })
