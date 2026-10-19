namespace RadioHomeEngine

open System
open System.Collections.Generic
open System.Threading
open System.Threading.Channels
open System.Threading.Tasks

type RemoteSegmentSourceAdapter(source: IRemoteSegmentSource) =
    let internalBuffer = Channel.CreateBounded<DownstreamSegment>(3)

    let adapterCancellationTokenSource = new CancellationTokenSource()

    let activeTask = task {
        let cancellationToken = adapterCancellationTokenSource.Token

        try
            let mutable isComplete = false
            while not isComplete && not cancellationToken.IsCancellationRequested do
                isComplete <- source.IsComplete

                let! segmentsFound = source.ScanAsync(cancellationToken)
                for segment in segmentsFound do
                    printfn "[RemoteSegmentSourceAdapter] Found %A" segment.mediaSequence
                    do! internalBuffer.Writer.WriteAsync(segment)

                if segmentsFound = [] then
                    do! Task.Delay(source.RecommendedInterval)
        with :? OperationCanceledException when cancellationToken.IsCancellationRequested -> ()

        internalBuffer.Writer.Complete()
    }

    interface ILocalSegmentSource with
        member _.ReadNextAsync(cancellationToken) = task {
            let! more = internalBuffer.Reader.WaitToReadAsync(cancellationToken)

            if more then
                printfn "[RemoteSegmentSourceAdapter] Getting segment"
                let! segment = internalBuffer.Reader.ReadAsync(cancellationToken)
                printfn "[RemoteSegmentSourceAdapter] Returning segment %A" segment.mediaSequence
                return AsyncNextSegment segment
            else
                printfn "[RemoteSegmentSourceAdapter] No more segments"
                return AsyncNoMoreSegments
        }

        member _.DisposeAsync() = ValueTask (task {
            printfn "[RemoteSegmentSourceAdapter] Disposing"
            do! source.DisposeAsync()
            do! adapterCancellationTokenSource.CancelAsync()
            do! activeTask
        })
