namespace RadioHomeEngine

open System
open System.Threading.Tasks

type SampleRemoteStreamSource(url: string) =
    interface IProgramInformationSource with
        member this.GetCurrentProgramInformationAsync(_) = Task.FromResult({
            ChannelName = url
            Program = Program url
        })

    interface IRemoteStreamSource with
        member this.ProgramInformationSource = this
        member this.Url = url
