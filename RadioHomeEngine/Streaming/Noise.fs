namespace RadioHomeEngine

open System
open System.Diagnostics
open System.IO

module Noise =
    let private bitsPerSecond = 65536

    let getPlaylist () = String.concat "\n" [
        $"#EXTM3U"
        $"#EXT-X-ALLOW-CACHE:NO"
        $"#EXT-X-VERSION:1"
        $"#EXT-X-STREAM-INF:BANDWIDTH={bitsPerSecond},CODECS=\"mp4a.40.5\""
        $"chunklist.m3u8"
        $""
    ]

    let private segmentLengthSeconds = 10L

    let mutable private segments = []

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

    let private color = "brown"

    let private inputParameters = $"-f lavfi -i \"anoisesrc=sample_rate=44100:color={color}\""
    let private outputParameters = $"-f mpegts -c:a aac -ac 2 -b:a {bitsPerSecond} -"

    let private estimatedSegmentSize = lazy task {
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

    let private generatorProcess = lazy Process.Start(new ProcessStartInfo(
        $"ffmpeg",
        $"{inputParameters} {outputParameters}",
        RedirectStandardOutput = true))

    let getChunkAsync cancellationToken = task {
        let! length = estimatedSegmentSize.Value
        let data = Array.create length 0uy
        do! generatorProcess.Value.StandardOutput.BaseStream.ReadExactlyAsync(data, cancellationToken)
        return data
    }
