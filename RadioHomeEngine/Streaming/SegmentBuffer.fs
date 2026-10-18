namespace RadioHomeEngine

open System
open System.Threading
open System.Threading.Channels
open System.Threading.Tasks
open FSharp.Control

type SegmentBuffer(primaryFactory: ISegmentSourceFactory, cancellationToken: CancellationToken) =
    let mutable disposed = false

    let channel = Channel.CreateBounded<DownstreamSegment>(3)

    let localTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
    let localToken = localTokenSource.Token

    let mutable interruptions = []

    let getCurrentFactory () =
        interruptions
        |> List.tryHead
        |> Option.defaultValue primaryFactory

    let ingestionTask = task {
        while not localToken.IsCancellationRequested do
            let factory = getCurrentFactory ()

            use! segmentSource = factory.CreateAsync()

            let mutable isComplete = false

            while factory = getCurrentFactory () && not localToken.IsCancellationRequested && not isComplete do
                let! r = segmentSource.ReadNextAsync(localToken)
                match r with
                | NextSegment segment ->
                    printfn "[SegmentBuffer] Writing: %A" segment.mediaSequence
                    do! channel.Writer.WriteAsync(segment, localToken)
                    SegmentCacheActivityIndicator.notify ()
                    printfn "[SegmentBuffer] Written: %A" segment.mediaSequence
                | NoMoreSegments ->
                    isComplete <- true
                    if (Some factory = List.tryHead interruptions) then
                        interruptions <- List.tail interruptions

            printfn "[SegmentBuffer] Done reading from source: %A" segmentSource

        channel.Writer.Complete()

        SegmentCacheActivityIndicator.notify ()
    }

    member this.Interrupt(factory: ISegmentSourceFactory) =
        interruptions <- factory :: interruptions

    member this.IsInterrupted() =
        interruptions <> []

    member this.ClearAllInterruptions() =
        interruptions <- []

    member this.TryRead() =
        match channel.Reader.TryRead() with
        | (true, segment) -> Some segment
        | (false, _) -> None

    member this.ReadAll() = [
        let mutable more = true
        while more do
            match this.TryRead() with
            | Some segment ->
                yield segment
            | None ->
                more <- false
    ]

    member this.IsComplete = channel.Reader.Count = 0 && channel.Reader.Completion.IsCompleted

    interface IAsyncDisposable with
        member this.DisposeAsync() = new ValueTask(task {
            if not disposed then
                printfn "[SegmentBuffer] Disposing"

                disposed <- true

                localTokenSource.Cancel()

                try
                    do! ingestionTask
                with :? OperationCanceledException as ex ->
                    printfn "%A" ex

                let! leftoverSegments = channel.Reader.ReadAllAsync() |> AsyncSeq.toListAsync
                ignore leftoverSegments
        })
