namespace RadioHomeEngine

open System
open System.Threading
open System.Threading.Tasks

type SegmentReadResult =
| NextSegment of DownstreamSegment
| NoMoreSegments

type ILocalSegmentSource =
    inherit IAsyncDisposable
    abstract member ReadNextAsync: cancellationToken: CancellationToken -> Task<SegmentReadResult>
