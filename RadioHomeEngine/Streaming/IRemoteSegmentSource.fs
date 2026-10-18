namespace RadioHomeEngine

open System
open System.Threading
open System.Threading.Tasks

type IRemoteSegmentSource =
    inherit IAsyncDisposable
    abstract member IsComplete: bool
    abstract member RecommendedInterval: TimeSpan
    abstract member ScanAsync: cancellationToken: CancellationToken -> Task<DownstreamSegment list>
