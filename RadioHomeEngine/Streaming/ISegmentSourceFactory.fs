namespace RadioHomeEngine

open System.Threading.Tasks

type ISegmentSourceFactory =
    abstract member ProgramInformationSource: IProgramInformationSource
    abstract member CreateAsync: unit -> Task<ISegmentSource>
