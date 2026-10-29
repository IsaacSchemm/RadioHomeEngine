namespace RadioHomeEngine

open System.Threading.Tasks

/// An object which has the configuration settings necessary to construct a new ISegmentSource for a specific media source.
type ISegmentSourceFactory =
    abstract member ChannelInformation: ChannelInformation
    abstract member ProgramInformationSource: IProgramInformationSource
    abstract member CreateAsync: unit -> Task<ISegmentSource>
