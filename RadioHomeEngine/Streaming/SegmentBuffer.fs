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

            use! createdSegmentSource = factory.CreateAsync()

            match createdSegmentSource with
            | LocalSegmentSource segmentSource ->
                raise (new NotImplementedException())
            | RemoteSegmentSource segmentSource ->
                while factory = getCurrentFactory () && not localToken.IsCancellationRequested do
                    let isComplete = segmentSource.IsComplete

                    let! segments = segmentSource.ScanAsync(localToken)

                    for segment in segments do
                        printfn "[SegmentBuffer] Writing: %A" segment.mediaSequence
                        do! channel.Writer.WriteAsync(segment, localToken)
                        printfn "[SegmentBuffer] Written: %A" segment.mediaSequence

                    if segments <> [] then
                        SegmentCacheActivityIndicator.notify ()

                    do! Task.Delay(segmentSource.RecommendedInterval, localToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing)

                    match isComplete, interruptions with
                    | (true, _ :: tail) -> interruptions <- tail
                    | _ -> ()

            printfn "[SegmentBuffer] Complete: %A" createdSegmentSource

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
                disposed <- true

                localTokenSource.Cancel()

                do! ingestionTask

                let! leftoverSegments = channel.Reader.ReadAllAsync() |> AsyncSeq.toListAsync
                ignore leftoverSegments
        })
