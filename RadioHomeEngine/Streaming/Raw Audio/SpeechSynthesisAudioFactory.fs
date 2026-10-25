namespace RadioHomeEngine

open System
open System.Diagnostics
open System.IO
open System.Threading
open System.Threading.Tasks
open FSharp.Control

type SpeechSynthesisAudioFactory(title: string, text: string) =
    let generator = lazy task {
        try
            use proc =
                new ProcessStartInfo(
                    "espeak",
                    "--stdin --stdout -v us-mbrola-2 -s 140 -a 150",
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true)
                |> Process.Start

            use writeTask = task {
                use stream = proc.StandardInput
                do! stream.WriteAsync(text)
            }

            use readTask = task {
                use ms = new MemoryStream()
                use stream = proc.StandardOutput
                do! stream.BaseStream.CopyToAsync(ms)
                return ms.ToArray()
            }

            do! writeTask
            return! readTask
        with ex when File.Exists("C:/Windows/Media/chord.wav") ->
            printfn "%A" ex
            return! File.ReadAllBytesAsync("C:/Windows/Media/chord.wav")
    }

    let encoder = lazy task {
        use proc =
            new ProcessStartInfo(
                "ffmpeg",
                "-f wav -i - -f s16le -ar 44100 -ac 2 -",
                RedirectStandardInput = true,
                RedirectStandardOutput = true)
            |> Process.Start

        use writeTask = task {
            let! wave = generator.Value
            use stream = proc.StandardInput.BaseStream
            do! stream.WriteAsync(wave)
        }

        use readTask = task {
            use ms = new MemoryStream()
            use stream = proc.StandardOutput
            do! stream.BaseStream.CopyToAsync(ms)
            return ms.ToArray()
        }

        do! writeTask
        return! readTask
    }

    interface IRawAudioFactory with
        member this.ProgramInformation = {
            channelName = "Message"
            title = title
            artists = []
            albums = []
        }

        member this.CreateAsync() = task {
            let! wave = encoder.Value

            return {
                new IRawAudio with
                    member _.Stream = new MemoryStream(wave, writable = false)
                    member _.DisposeAsync() = ValueTask()
            }
        }
