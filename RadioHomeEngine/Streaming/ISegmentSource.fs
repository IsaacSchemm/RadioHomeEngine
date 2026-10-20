namespace RadioHomeEngine

open System
open System.Threading
open System.Threading.Tasks

type ISegmentSource =
    inherit IAsyncDisposable
    abstract member ProgramInformationSource: IProgramInformationSource
    abstract member ReadNextAsync: cancellationToken: CancellationToken -> Task<SegmentReadResult>
