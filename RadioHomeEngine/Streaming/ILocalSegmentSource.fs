namespace RadioHomeEngine

open System
open System.Threading
open System.Threading.Tasks

type SegmentReadResult =
| NextSegment of DownstreamSegment
| WaitingForMoreSegments
| NoMoreSegments

type AsyncSegmentReadResult =
| AsyncNextSegment of DownstreamSegment
| AsyncNoMoreSegments

type ILocalSegmentSource =
    inherit IAsyncDisposable
    abstract member ReadNextAsync: cancellationToken: CancellationToken -> Task<AsyncSegmentReadResult>
    abstract member TryReadNext: unit -> SegmentReadResult
