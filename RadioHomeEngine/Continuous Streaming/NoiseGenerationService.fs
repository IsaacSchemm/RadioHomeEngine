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
    let sampleRate = 44100
    let color = "brown"
    let chunklistFile = "chunklist.m3u8"

    let inputParameters = String.concat " " [
        "-f lavfi"
        $"-i \"anoisesrc=sample_rate={sampleRate}:color={color}\""
    ]

    let outputParameters = String.concat " " [
        "-f hls"
        "-hls_time 10"
        "-hls_segment_type mpegts"
        "-hls_flags delete_segments+program_date_time+temp_file"
        "-c:a aac"
        "-ac 2"
        $"-b:a {bitsPerSecond}"
        Path.Combine(path, chunklistFile)
    ]

    let GetFiles(filenames: string seq) = [
        let utf8 str = Encoding.UTF8.GetBytes(String.concat "\n" str)

        for filename in filenames do
            let path = Path.Combine(path, filename)

            match filename with
            | "playlist.m3u8" ->
                {|
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
            | "chunklist.m3u8" ->
                {|
                    data = File.ReadAllBytes(path)
                    contentType = "application/x-mpegURL"
                |}
            | _ when filename.EndsWith(".ts") && File.Exists(path) ->
                {|
                    data = File.ReadAllBytes(path)
                    contentType = "video/mp2t"
                |}
            | _ -> ()
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
            $"-f f32le -i - {NoiseGenerationService.outputParameters}",
            RedirectStandardInput = true,
            RedirectStandardOutput = true))

        let inputControl = generator.StandardInput

        do! task {
            use pipeIn = generator.StandardOutput.BaseStream
            use pipeOut = encoder.StandardInput.BaseStream

            let segmentTime = TimeSpan.FromSeconds(10L)
            let segmentSize = NoiseGenerationService.sampleRate * 4 * int segmentTime.TotalSeconds
            let buffer = Array.create segmentSize 0uy

            let transferDataAsync () = task {
                try
                    do! pipeIn.ReadExactlyAsync(buffer, cancellationToken)
                    do! pipeOut.WriteAsync(buffer, cancellationToken)
                with :? EndOfStreamException -> ()
            }

            //for _ in 1 .. 3 do
            //    do! transferDataAsync ()

            use timer = new Timers.Timer(segmentTime)
            timer.Enabled <- true
            timer.Elapsed.Add(fun _ -> transferDataAsync () |> ignore)

            timer.Enabled <- false

            while not generator.HasExited && not encoder.HasExited && not cancellationToken.IsCancellationRequested do
                do! transferDataAsync ()
                do! Task.Delay(TimeSpan.FromSeconds(5L), cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing)
        }

        if not generator.HasExited then
            inputControl.Write('q')
            do! inputControl.DisposeAsync()

        do! generator.WaitForExitAsync()
        do! encoder.WaitForExitAsync()

        Directory.Delete(NoiseGenerationService.path, recursive = true)
    }
