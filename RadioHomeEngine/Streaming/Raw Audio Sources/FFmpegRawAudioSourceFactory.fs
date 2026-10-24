namespace RadioHomeEngine

open System
open System.Diagnostics
open System.IO
open System.Threading
open System.Threading.Tasks
open FSharp.Control

type FFmpegRawAudioSourceFactory(sourceParameters: FFmpegRawAudioSourceParameters) =
    interface IRawAudioSourceFactory with
        member _.ProgramInformationSource = sourceParameters.ProgramInformationSource

        member _.CreateAsync() = task {
            let mutable disposed = false

            //let color = "brown"

            let parameters = String.concat " " [
                "-nostats"
                "-hide_banner"
                "-loglevel warning"
                sourceParameters.InputParameters
                //"-f lavfi"
                //sprintf "-i \"anoisesrc=sample_rate=44100:color=%s\"" color
                "-f f64le"
                "-ac 2"
                "-"
            ]

            let generator = Process.Start(new ProcessStartInfo(
                "ffmpeg",
                parameters,
                RedirectStandardInput = true,
                RedirectStandardOutput = true))

            return {
                new IRawAudioSource with
                    member _.ProgramInformationSource = sourceParameters.ProgramInformationSource

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
