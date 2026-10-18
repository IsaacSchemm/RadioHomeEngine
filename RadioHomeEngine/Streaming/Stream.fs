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

    let popAsync () = task {
        match bufferStack with
        | [] -> ()
        | buffer :: others ->
            printfn "[Stream] Removing from stack [%d]" definition.channelNumber
            bufferStack <- others
            do! (buffer :> IAsyncDisposable).DisposeAsync()
    }

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

    member _.ReturnToRootAsync() = task {
        while bufferStack.Length > 1 do
            do! popAsync ()
    }

    member _.ReadFromBufferAsync() = task {
        match bufferStack with
        | [] -> ()
        | buffer :: _ ->
            cache.ReadFrom(buffer)

            if buffer.IsComplete then
                do! popAsync ()

        cache.EvictStale()
    }

    member this.IsComplete =
        bufferStack |> List.forall (fun buffer -> buffer.IsComplete)

    member this.StopAsync() = task {
        while bufferStack.Length > 0 do
            do! popAsync ()
    }

    interface IAsyncDisposable with
        member this.DisposeAsync() = ValueTask (task {
            do! this.StopAsync()
        })
