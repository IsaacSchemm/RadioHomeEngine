namespace RadioHomeEngine

open System
open System.Threading
open System.Threading.Tasks

type SegmentReadResult = Segment of DownstreamSegment | Complete

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
