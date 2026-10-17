namespace RadioHomeEngine

open System
open System.Threading
open System.Threading.Channels
open System.Threading.Tasks
open FSharp.Control

type SegmentBuffer(source: ISegmentSource, cancellationToken: CancellationToken) =
    let guid = Guid.NewGuid()

    let mutable disposed = false

    let channel = Channel.CreateBounded<DownstreamSegment>(5)

    let localTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
    let localToken = localTokenSource.Token

    let ingestionTask = task {
        use segmentSource = source
        while not localToken.IsCancellationRequested do
            let! segments = segmentSource.ScanAsync(localToken)

            for segment in segments do
                do! channel.Writer.WriteAsync(segment, localToken)

            if segments <> [] then
                SegmentCacheActivityIndicator.notify ()

            do! Task.Delay(TimeSpan.FromSeconds(1.0))
    }

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

    interface IAsyncDisposable with
        member this.DisposeAsync() = new ValueTask(task {
            if not disposed then
                disposed <- true

                localTokenSource.Cancel()

                do! ingestionTask

                channel.Writer.Complete()

                let! leftoverSegments = channel.Reader.ReadAllAsync() |> AsyncSeq.toListAsync
                ignore leftoverSegments
        })
