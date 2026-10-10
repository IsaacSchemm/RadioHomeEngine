namespace RadioHomeEngine

open System
open System.Diagnostics
open System.IO
open System.Text
open System.Threading.Tasks
open Microsoft.Extensions.Hosting

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
        "-hls_flags delete_segments+program_date_time+temp_file"
        "-c:a aac"
        "-ac 2"
        $"-b:a {bitsPerSecond}"
        Path.Combine(path, "chunklist.m3u8")
    ]

    let readSpeedParameters = String.concat " " [
        "-readrate 1"
    ]

    let mutable lastAccess = DateTimeOffset.UtcNow

    let isActive () =
        DateTimeOffset.UtcNow - lastAccess < TimeSpan.FromMinutes(1L)

    exception InvalidFilenameException

    let getFileAsync filename cancellationToken = task {
        lastAccess <- DateTimeOffset.UtcNow

        let utf8 str = Encoding.UTF8.GetBytes(String.concat "\n" str)

        let path = Path.Combine(path, filename)

        match filename with
        | "playlist.m3u8" ->
            return {|
                data = utf8 [
                    "#EXTM3U"
                    "#EXT-X-ALLOW-CACHE:NO"
                    "#EXT-X-VERSION:1"
                    $"#EXT-X-STREAM-INF:BANDWIDTH={bitsPerSecond},CODECS=\"mp4a.40.5\""
                    "chunklist.m3u8"
                    ""
                ]
                contentType = "application/x-mpegURL"
            |}
        | "chunklist.m3u8" when File.Exists(path) ->
            let! data = File.ReadAllBytesAsync(path, cancellationToken)
            return {|
                data = data
                contentType = "application/x-mpegURL"
            |}
        | "chunklist.m3u8" ->
            return {|
                data = utf8 [
                    "#EXTM3U"
                    "#EXT-X-VERSION:3"
                    $"#EXT-X-TARGETDURATION:{segmentTimeSeconds}"
                    "#EXT-X-MEDIA-SEQUENCE:0"
                    ""
                ]
                contentType = "application/x-mpegURL"
            |}
        | _ when filename.EndsWith(".ts") && File.Exists(path) ->
            let! data = File.ReadAllBytesAsync(path, cancellationToken)
            return {|
                data = data
                contentType = "video/mp2t"
            |}
        | _ when filename.EndsWith(".ts") ->
            let! data = Silence.generateSegmentAsync (TimeSpan.FromSeconds(segmentTimeSeconds))
            return {|
                data = data
                contentType = "video/mp2t"
            |}
        | _ ->
            return raise InvalidFilenameException
    }

type NoiseGenerationService() =
    inherit BackgroundService()

    override _.ExecuteAsync(cancellationToken) = task {
        Directory.CreateDirectory(Noise.path) |> ignore

        use generator = Process.Start(new ProcessStartInfo(
            $"ffmpeg",
            $"{Noise.inputParameters} -nostats -hide_banner -loglevel warning -f f32le -",
            RedirectStandardInput = true,
            RedirectStandardOutput = true))

        use encoder = Process.Start(new ProcessStartInfo(
            $"ffmpeg",
            $"{Noise.readSpeedParameters} -nostats -hide_banner -loglevel warning -f f32le -i - {Noise.outputParameters}",
            RedirectStandardInput = true,
            RedirectStandardOutput = true))

        let inputControl = generator.StandardInput

        do! task {
            use pipeIn = generator.StandardOutput.BaseStream
            use pipeOut = encoder.StandardInput.BaseStream

            let bufferTime = TimeSpan.FromSeconds(10L)
            let bufferSize = Noise.sampleRate * 4 * int bufferTime.TotalSeconds
            let buffer = Array.create bufferSize 0uy

            while not generator.HasExited && not encoder.HasExited && not cancellationToken.IsCancellationRequested do
                try
                    if Noise.isActive () then
                        do! pipeIn.ReadExactlyAsync(buffer, cancellationToken)
                        do! pipeOut.WriteAsync(buffer, cancellationToken)
                    else
                        do! Task.Delay(TimeSpan.FromSeconds(Noise.segmentTimeSeconds / 2.0), cancellationToken)
                with ex ->
                    Console.Error.WriteLine(ex)
        }

        if not generator.HasExited then
            do! inputControl.WriteAsync('q')
            do! inputControl.DisposeAsync()

        do! generator.WaitForExitAsync()
        do! encoder.WaitForExitAsync()

        Directory.Delete(Noise.path, recursive = true)
    }
