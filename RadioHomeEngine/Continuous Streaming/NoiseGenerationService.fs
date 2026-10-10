namespace RadioHomeEngine

open System
open System.Diagnostics
open System.IO
open System.Text
open System.Threading.Tasks
open Microsoft.Extensions.Hosting

module NoiseGenerationService =
    let path = Path.Combine([|
        Path.GetTempPath()
        $"RadioHomeEngine-Noise-{Guid.NewGuid()}"
    |])

    let bitsPerSecond = 65536
    let color = "brown"
    let chunklistFile = "chunklist.m3u8"
    let sampleRate = 44100
    let segmentTimeSeconds = 10

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
        Path.Combine(path, chunklistFile)
    ]

    let readSpeedParameters = String.concat " " [
        "-readrate 1"
        "-readrate_catchup 2"
    ]

    let mutable enabled = true

    let getFiles (filenames: string seq) = [
        let utf8 str = Encoding.UTF8.GetBytes(String.concat "\n" str)

        for filename in filenames do
            let path = Path.Combine(path, filename)

            if filename = "playlist.m3u8" then {|
                data = utf8 [
                    "#EXTM3U"
                    "#EXT-X-ALLOW-CACHE:NO"
                    "#EXT-X-VERSION:1"
                    $"#EXT-X-STREAM-INF:BANDWIDTH={bitsPerSecond},CODECS=\"mp4a.40.5\""
                    chunklistFile
                    ""
                ]
                contentType = "application/x-mpegURL"
            |}
            else if filename = chunklistFile then  {|
                data =
                    if File.Exists(path)
                    then File.ReadAllBytes(path)
                    else utf8 [
                        "#EXTM3U"
                        "#EXT-X-VERSION:3"
                        $"#EXT-X-TARGETDURATION:{segmentTimeSeconds}"
                        "#EXT-X-MEDIA-SEQUENCE:0"
                        ""
                    ]
                contentType = "application/x-mpegURL"
            |}
            else if filename.EndsWith(".ts") && File.Exists(path) then {|
                data = File.ReadAllBytes(path)
                contentType = "video/mp2t"
            |}
    ]

type NoiseGenerationService() =
    inherit BackgroundService()

    override _.ExecuteAsync(cancellationToken) = task {
        Directory.CreateDirectory(NoiseGenerationService.path) |> ignore

        use generator = Process.Start(new ProcessStartInfo(
            $"ffmpeg",
            $"{NoiseGenerationService.inputParameters} -nostats -f f32le -",
            RedirectStandardInput = true,
            RedirectStandardOutput = true))

        use encoder = Process.Start(new ProcessStartInfo(
            $"ffmpeg",
            $"{NoiseGenerationService.readSpeedParameters} -nostats -f f32le -i - {NoiseGenerationService.outputParameters}",
            RedirectStandardInput = true,
            RedirectStandardOutput = true))

        let inputControl = generator.StandardInput

        do! task {
            use pipeIn = generator.StandardOutput.BaseStream
            use pipeOut = encoder.StandardInput.BaseStream

            let bufferTime = TimeSpan.FromSeconds(10L)
            let bufferSize = NoiseGenerationService.sampleRate * 4 * int bufferTime.TotalSeconds
            let buffer = Array.create bufferSize 0uy

            while not generator.HasExited && not encoder.HasExited && not cancellationToken.IsCancellationRequested do
                try
                    if NoiseGenerationService.enabled then
                        do! pipeIn.ReadExactlyAsync(buffer, cancellationToken)
                        do! pipeOut.WriteAsync(buffer, cancellationToken)
                    else
                        do! Task.Delay(TimeSpan.FromSeconds(5L), cancellationToken)
                with _ when generator.HasExited || encoder.HasExited -> ()
        }

        if not generator.HasExited then
            inputControl.Write('q')
            do! inputControl.DisposeAsync()

        do! generator.WaitForExitAsync()
        do! encoder.WaitForExitAsync()

        Directory.Delete(NoiseGenerationService.path, recursive = true)
    }
