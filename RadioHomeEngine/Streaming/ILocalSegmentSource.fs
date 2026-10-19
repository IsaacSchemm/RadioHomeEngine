namespace RadioHomeEngine

open System
open System.Threading
open System.Threading.Tasks

type AsyncSegmentReadResult =
| AsyncNextSegment of DownstreamSegment
| AsyncNoMoreSegments

type ILocalSegmentSource =
    inherit IAsyncDisposable
    abstract member ReadNextAsync: cancellationToken: CancellationToken -> Task<AsyncSegmentReadResult>
