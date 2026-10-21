namespace RadioHomeEngine

open System
open System.Threading
open System.Threading.Tasks

/// Information about a radio channel and what is playing on it.
type ProgramInformation = {
    channelName: string
    title: string
    artists: string list
    albums: string list
}

/// An object which provides information about a radio channel and what is playing on it.
type IProgramInformationSource =
    abstract member GetCurrentProgramInformationAsync: CancellationToken -> Task<ProgramInformation>
