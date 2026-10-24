namespace RadioHomeEngine

open System
open System.Threading.Tasks

type IRawAudioFactory =
    abstract member CreateAsync: unit -> Task<IRawAudio>
    abstract member ProgramInformation: ProgramInformation
