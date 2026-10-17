namespace RadioHomeEngine

open System
open System.Diagnostics
open System.IO
open System.Text
open System.Threading.Tasks
open Microsoft.Extensions.Hosting

module Silence =
    let bitsPerSecond = 65536

    let segmentTask = lazy task {
        let ffmpeg =
            new ProcessStartInfo(
                "ffmpeg",
                $"-nostats -hide_banner -loglevel warning -f lavfi -i anullsrc=cl=stereo:sample_rate=44100 -t 10 -c:a aac -b:a {bitsPerSecond} -f mpegts -",
                RedirectStandardOutput = true)
            |> Process.Start

        use buffer = new MemoryStream()

        let readTask = ffmpeg.StandardOutput.BaseStream.CopyToAsync(buffer)

        do! readTask
        do! ffmpeg.WaitForExitAsync()

        return buffer.ToArray()
    }

    segmentTask.Force() |> ignore

    let getSegment () =
        segmentTask.Value.GetAwaiter().GetResult()
