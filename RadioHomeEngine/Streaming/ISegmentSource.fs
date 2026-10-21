namespace RadioHomeEngine

open System
open System.Threading
open System.Threading.Tasks

/// The result of waiting for a new HLS media segment from the source.
type SegmentReadResult =
| NextSegment of DownstreamSegment
| NoMoreSegments

/// An object that provides a stream of HLS media segments.
type ISegmentSource =
    inherit IAsyncDisposable
    abstract member ProgramInformationSource: IProgramInformationSource
    abstract member ReadNextAsync: cancellationToken: CancellationToken -> Task<SegmentReadResult>
