namespace RadioHomeEngine

open System
open System.Threading.Tasks
open System.Collections.Generic

type IRawAudioSourceFactory =
    abstract member CreateAsync: unit -> Task<IRawAudioSource>
    abstract member ProgramInformationSource: IProgramInformationSource
