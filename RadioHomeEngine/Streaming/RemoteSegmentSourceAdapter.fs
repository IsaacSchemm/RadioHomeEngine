namespace RadioHomeEngine

open System
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks

type RemoteSegmentSourceAdapter(source: IRemoteSegmentSource) =
    let internalBuffer = new Queue<DownstreamSegment>()

    interface ILocalSegmentSource with
        member _.ReadNextAsync(cancellationToken) = task {
            let mutable isComplete = false

            while internalBuffer.Count = 0 && not isComplete && not cancellationToken.IsCancellationRequested do
                isComplete <- source.IsComplete

                let! segmentsFound = source.ScanAsync(cancellationToken)
                for segment in segmentsFound do
                    printfn "[RemoteSegmentSourceAdapter] Found %A" segment.mediaSequence
                    internalBuffer.Enqueue(segment)

                if segmentsFound = [] then
                    do! Task.Delay(source.RecommendedInterval)

            match internalBuffer.TryDequeue() with
            | (true, segment) ->
                printfn "[RemoteSegmentSourceAdapter] Returning %A" segment.mediaSequence
                return NextSegment segment
            | (false, _) ->
                printfn "[RemoteSegmentSourceAdapter] No more segments"
                return NoMoreSegments
        }

        member _.DisposeAsync() =
            source.DisposeAsync()
