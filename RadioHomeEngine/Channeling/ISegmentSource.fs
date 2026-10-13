namespace RadioHomeEngine

open System
open System.Threading
open System.Threading.Tasks

type ISegmentSource =
    inherit IAsyncDisposable
    abstract member ScanAsync: cancellationToken: CancellationToken -> Task<ChanneledSegment list>
