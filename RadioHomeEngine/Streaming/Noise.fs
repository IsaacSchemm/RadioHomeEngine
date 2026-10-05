namespace RadioHomeEngine

open System
open System.Diagnostics
open System.IO

module Noise =
    let bitsPerSecond = 65536
    let color = "brown"

    let getPlaylist () = String.concat "\n" [
        $"#EXTM3U"
        $"#EXT-X-ALLOW-CACHE:NO"
        $"#EXT-X-VERSION:1"
        $"#EXT-X-STREAM-INF:BANDWIDTH={bitsPerSecond},CODECS=\"mp4a.40.5\""
        $"chunklist.m3u8"
        $""
    ]

    let segmentLengthSeconds = 60L

    let getChunklist () = String.concat "\n" [
        let sequenceNumber = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / segmentLengthSeconds

        $"#EXTM3U"
        $"#EXT-X-TARGETDURATION:{segmentLengthSeconds}"
        $"#EXT-X-VERSION:1"
        $"#EXT-X-ALLOW-CACHE:NO"
        $"#EXT-X-MEDIA-SEQUENCE:{sequenceNumber}"
        for i in [0L .. 2L] do
            $"#EXTINF:{segmentLengthSeconds},"
            $"chunk-{sequenceNumber + i}.ts"
        ""
    ]

    let inputParameters = $"-f lavfi -i \"anoisesrc=sample_rate=44100:color={color}\""
    let outputParameters = $"-f mpegts -c:a aac -ac 1 -b:a {bitsPerSecond} -"

    let segmentLengthBytes = lazy task {
        let psi = new ProcessStartInfo(
            $"ffmpeg",
            $"{inputParameters} -t {segmentLengthSeconds} {outputParameters}",
            RedirectStandardOutput = true)
        use p = Process.Start(psi)
        use ms = new MemoryStream()
        do! p.StandardOutput.BaseStream.CopyToAsync(ms)
        do! p.WaitForExitAsync()
        return int ms.Length
    }

    let generatorProcess = lazy (
        let psi = new ProcessStartInfo(
            $"ffmpeg",
            $"{inputParameters} {outputParameters}",
            RedirectStandardOutput = true)
        Process.Start(psi)
    )

    let getChunkAsync cancellationToken = task {
        let! length = segmentLengthBytes.Value
        let data = Array.create length 0uy
        do! generatorProcess.Value.StandardOutput.BaseStream.ReadExactlyAsync(data, cancellationToken)
        return data
    }
