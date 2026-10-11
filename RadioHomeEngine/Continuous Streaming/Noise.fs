namespace RadioHomeEngine

open System
open System.Collections.Generic
open System.Diagnostics
open System.IO
open System.Text
open System.Threading.Channels
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open System.Threading

module Noise =
    let path = Path.Combine([|
        Path.GetTempPath()
        $"RadioHomeEngine-Noise-{Guid.NewGuid()}"
    |])

    let bitsPerSecond = 65536
    let color = "brown"
    let sampleRate = 44100
    let segmentTimeSeconds = 10.0

    let inputParameters = String.concat " " [
        "-f lavfi"
        $"-i \"anoisesrc=sample_rate={sampleRate}:color={color}\""
    ]

    let outputParameters = String.concat " " [
        "-f hls"
        $"-hls_time {segmentTimeSeconds}"
        "-hls_segment_type mpegts"
        "-hls_flags program_date_time+temp_file"
        "-c:a aac"
        "-ac 2"
        $"-b:a {bitsPerSecond}"
        Path.Combine(path, "chunklist.m3u8")
    ]

    let readSpeedParameters = String.concat " " [
        "-readrate 1"
    ]

    let channel = Channel.CreateBounded<GeneratedSegment>(5)

    exception ChannelFullException

    let ingestSegmentsAsync cancellationToken = task {
        let segments = [
            for file in Directory.EnumerateFiles(path, "*.ts") do
                let data = File.ReadAllBytes(file)
                let info = new FileInfo(file)

                yield {
                    dateTime = new DateTimeOffset(info.CreationTime)
                    duration = segmentTimeSeconds
                    data = data
                    originalPath = file
                }
        ]

        for segment in segments |> Seq.sortBy (fun s -> s.dateTime) do
            File.Delete(segment.originalPath)
            if not (channel.Writer.TryWrite(segment)) then
                raise ChannelFullException
    }

    let streamAsync (cancellationToken: CancellationToken) = task {
        Directory.CreateDirectory(path) |> ignore

        use encoder = Process.Start(new ProcessStartInfo(
            $"ffmpeg",
            $"{readSpeedParameters} {inputParameters} {outputParameters}",
            RedirectStandardInput = true))

        let mutable available = true

        while available && not encoder.HasExited do
            try
                do! ingestSegmentsAsync cancellationToken
                do! Task.Delay(TimeSpan.FromSeconds(2L), cancellationToken)
            with :? ChannelFullException ->
                available <- false

        if not encoder.HasExited then
            use inputControl = encoder.StandardInput
            do! inputControl.WriteAsync('q')

        do! encoder.WaitForExitAsync(cancellationToken)

        Directory.Delete(path, recursive = true)
    }

type NoiseGenerationService() =
    inherit BackgroundService()

    override _.ExecuteAsync(cancellationToken) = task {
        let mutable active = true

        while active && not cancellationToken.IsCancellationRequested do
            do! Noise.streamAsync cancellationToken

            printfn "[Noise] Waiting"

            let! writable = Noise.channel.Writer.WaitToWriteAsync(cancellationToken)
            active <- writable
    }
