namespace RadioHomeEngine

open System
open System.Threading.Tasks

type IRemoteStreamSource =
    abstract member Name: string
    abstract member ProgramInformationSource: IProgramInformationSource
    abstract member Url: string
