namespace RadioHomeEngine

open System
open System.Diagnostics
open System.IO

module Noise =
    let bitsPerSecond = 65536
    let color = "brown"

    let mutable segments = []

    let getPlaylist () = String.concat "\n" [
        $"#EXTM3U"
        $"#EXT-X-ALLOW-CACHE:NO"
        $"#EXT-X-VERSION:1"
        $"#EXT-X-STREAM-INF:BANDWIDTH={bitsPerSecond},CODECS=\"mp4a.40.5\""
        $"chunklist.m3u8"
        $""
    ]

    let segmentLengthSeconds = 10L

    let getChunklist () = String.concat "\n" [
        let sequenceNumber = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 10L
        let dateTime = DateTimeOffset.FromUnixTimeSeconds(sequenceNumber * 10L)

        let newSegment = {| sequenceNumber = sequenceNumber; dateTime = dateTime |}

        if not (segments |> List.contains newSegment) then
            segments <- newSegment :: List.truncate 2 segments

        $"#EXTM3U"
        $"#EXT-X-TARGETDURATION:{segmentLengthSeconds}"
        $"#EXT-X-VERSION:1"
        $"#EXT-X-ALLOW-CACHE:NO"
        $"#EXT-X-MEDIA-SEQUENCE:{(List.last segments).sequenceNumber}"

        for segment in Seq.rev segments do
            $"#EXT-X-PROGRAM-DATE-TIME:{segment.dateTime:o}"
            $"#EXTINF:{segmentLengthSeconds},"
            $"chunk-{segment.sequenceNumber}.ts"

        ""
    ]

    let inputParameters = $"-f lavfi -i \"anoisesrc=sample_rate=44100:color={color}\""
    let outputParameters = $"-f mpegts -c:a aac -ac 2 -b:a {bitsPerSecond} -"

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
