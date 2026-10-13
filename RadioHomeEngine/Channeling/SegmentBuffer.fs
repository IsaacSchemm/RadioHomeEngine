namespace RadioHomeEngine

open System
open System.Threading
open System.Threading.Channels
open System.Threading.Tasks
open FSharp.Control

type SegmentBuffer(factory: unit -> ISegmentSource, cancellationToken: CancellationToken) =
    let channel = Channel.CreateBounded<ChanneledSegment>(5)

    let localTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
    let localToken = localTokenSource.Token

    let ingestionTask = task {
        use segmentSource = factory ()
        while not localToken.IsCancellationRequested do
            let! segments = segmentSource.ScanAsync(localToken)
            for segment in segments do
                do! channel.Writer.WriteAsync(segment, localToken)
            do! Task.Delay(TimeSpan.FromSeconds(1.0))
    }

    member this.TryRead() =
        match channel.Reader.TryRead() with
        | (true, segment) -> Some segment
        | (false, _) -> None

    interface IAsyncDisposable with
        member this.DisposeAsync() = new ValueTask(task {
            printfn "Disposing SegmentBuffer"

            localTokenSource.Cancel()

            do! ingestionTask

            channel.Writer.Complete()

            let! leftoverSegments = channel.Reader.ReadAllAsync() |> AsyncSeq.toListAsync
            ignore leftoverSegments
        })
