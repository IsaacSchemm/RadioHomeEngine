namespace RadioHomeEngine

open System
open System.Threading
open System.Threading.Tasks

type SegmentReadResult = NextSegment of DownstreamSegment | NoMoreSegments

type ILocalSegmentSource =
    inherit IAsyncDisposable
    abstract member ReadNextAsync: cancellationToken: CancellationToken -> Task<SegmentReadResult>

type IRemoteSegmentSource =
    inherit IAsyncDisposable
    abstract member IsComplete: bool
    abstract member RecommendedInterval: TimeSpan
    abstract member ScanAsync: cancellationToken: CancellationToken -> Task<DownstreamSegment list>

type SegmentSource =
| LocalSegmentSource of ILocalSegmentSource
| RemoteSegmentSource of IRemoteSegmentSource
with
    interface IAsyncDisposable with
        member this.DisposeAsync() =
            match this with
            | LocalSegmentSource source -> source.DisposeAsync()
            | RemoteSegmentSource source -> source.DisposeAsync()

type RemoteSegmentSourceAdapter(source: IRemoteSegmentSource) =
    let internalBuffer = new System.Collections.Generic.Queue<DownstreamSegment>()

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
