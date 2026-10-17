namespace RadioHomeEngine

open System
open System.Threading
open System.Threading.Tasks

type StreamMode =
| Resumable of (unit -> ISegmentSource)
| Ephemeral of ISegmentSource

type StreamDefinition = {
    channelNumber: int
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
        if bufferStack = [] then
            printfn "[Stream] Starting new buffer for %d" definition.channelNumber

            let source =
                match definition.mode with
                | Resumable factory -> factory ()
                | Ephemeral source -> source

            bufferStack <- new SegmentBuffer(source, cancellationToken) :: bufferStack
            lastToken <- cancellationToken
    }

    member _.PushAsync(source: ISegmentSource) = task {
        if bufferStack = [] then
            raise (new NotImplementedException())

        printfn "[Stream] Adding to stack [%d]" definition.channelNumber
        bufferStack <- new SegmentBuffer(source, lastToken) :: bufferStack
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
        | buffer :: _ -> cache.ReadFrom(buffer)
        | [] -> ()

        cache.EvictStale()

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
