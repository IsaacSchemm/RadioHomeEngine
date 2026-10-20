namespace RadioHomeEngine

open System
open System.Threading
open System.Threading.Tasks

type IProgramInformationSource =
    abstract member GetCurrentProgramInformationAsync: CancellationToken -> Task<ProgramInformation>
