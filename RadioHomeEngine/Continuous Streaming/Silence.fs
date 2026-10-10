namespace RadioHomeEngine

open System
open System.Diagnostics
open System.IO
open System.Text
open System.Threading.Tasks
open Microsoft.Extensions.Hosting

module Silence =
    let bitsPerSecond = 65536

    let generateSegmentAsync (length: TimeSpan) = task {
        let ffmpeg =
            new ProcessStartInfo(
                "ffmpeg",
                $"-nostats -hide_banner -loglevel warning -f lavfi -i anullsrc=cl=stereo:sample_rate=44100 -t {length.TotalSeconds} -c:a aac -b:a {bitsPerSecond} -f mpegts -",
                RedirectStandardOutput = true)
            |> Process.Start

        use buffer = new MemoryStream()

        let readTask = ffmpeg.StandardOutput.BaseStream.CopyToAsync(buffer)

        do! readTask
        do! ffmpeg.WaitForExitAsync()

        return buffer.ToArray()
    }

    let segmentTimeSeconds = 10.0

    let zeroTime = DateTimeOffset.UtcNow

    exception InvalidFilenameException

    let getFileAsync filename cancellationToken = task {
        let utf8 str = Encoding.UTF8.GetBytes(String.concat "\n" str)

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
        | "chunklist.m3u8" ->
            let elapsed = DateTimeOffset.UtcNow - zeroTime
            let segmentCount = floor (elapsed.TotalSeconds / 10.0)

            let lastSegmentTime = zeroTime + TimeSpan.FromSeconds(segmentCount * 10.0)
            let lastSequenceNumber = int64 segmentCount

            let segmentTimes = [
                lastSegmentTime - TimeSpan.FromSeconds(20.0)
                lastSegmentTime - TimeSpan.FromSeconds(10.0)
                lastSegmentTime
            ]

            let sequenceNumbers = [
                lastSequenceNumber - 2L
                lastSequenceNumber - 1L
                lastSequenceNumber
            ]

            return {|
                data = utf8 [
                    "#EXTM3U"
                    "#EXT-X-VERSION:3"
                    $"#EXT-X-TARGETDURATION:{segmentTimeSeconds}"
                    $"#EXT-X-MEDIA-SEQUENCE:{lastSequenceNumber}"
                    for (segmentTime, sequenceNumber) in Seq.zip segmentTimes sequenceNumbers do
                        if sequenceNumber > 0 then
                            $"#EXT-X-PROGRAM-DATE-TIME:{segmentTime:o}"
                            $"#EXTINF:{segmentTimeSeconds},"
                            $"chunk-{sequenceNumber}.ts"
                    ""
                ]
                contentType = "application/x-mpegURL"
            |}
        | _ when filename.EndsWith(".ts") ->
            let! data = generateSegmentAsync (TimeSpan.FromSeconds(segmentTimeSeconds))
            return {|
                data = data
                contentType = "video/mp2t"
            |}
        | _ ->
            return raise InvalidFilenameException
    }
