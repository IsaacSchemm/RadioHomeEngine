namespace RadioHomeEngine

open System
open System.Threading
open System.Threading.Tasks

/// An object which provides information about a radio channel and what is playing on it.
type IProgramInformationSource =
    abstract member GetCurrentProgramInformationAsync: CancellationToken -> Task<ProgramInformation>
