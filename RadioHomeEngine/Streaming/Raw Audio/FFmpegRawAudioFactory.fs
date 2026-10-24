namespace RadioHomeEngine

open System
open System.Diagnostics
open System.IO
open System.Threading
open System.Threading.Tasks
open FSharp.Control

[<AbstractClass>]
type FFmpegRawAudioFactory() =
    abstract member InputParameters: string seq
    abstract member ProgramInformation: ProgramInformation

    interface IRawAudioFactory with
        member this.ProgramInformation = this.ProgramInformation

        member this.CreateAsync() = task {
            let mutable disposed = false

            //let color = "brown"

            let parameters = [
                "-nostats"
                "-hide_banner"
                "-loglevel"; "warning"
                yield! this.InputParameters
                "-f"; "f64le"
                "-ac"; "2"
                "-"
            ]

            let generator = Process.Start(new ProcessStartInfo(
                "ffmpeg",
                parameters,
                RedirectStandardInput = true,
                RedirectStandardOutput = true))

            return {
                new IRawAudio with
                    member _.Stream = generator.StandardOutput.BaseStream

                    member _.DisposeAsync() = new ValueTask(task {
                        printfn "[FFmpegRawAudioSource] Disposing"

                        disposed <- true

                        if not generator.HasExited then
                            use inputControl = generator.StandardInput
                            do! inputControl.WriteAsync('q')

                            use outputStream = generator.StandardOutput.BaseStream
                            ignore outputStream

                        printfn "[FFmpegRawAudioSource] Waiting"
                        do! generator.WaitForExitAsync()

                        printfn "[FFmpegRawAudioSource] Disposed"
                    })
            }
        }
