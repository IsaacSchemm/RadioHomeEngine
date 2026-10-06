namespace RadioHomeEngine

open System
open System.Diagnostics
open System.IO

module Noise =
    let private bitsPerSecond = 65536

    let path = Path.Combine(
        Path.GetTempPath(),
        "RadioHomeEngine-Noise")

    if Directory.Exists(path) then
        Directory.Delete(path, recursive = true)

    Directory.CreateDirectory(path) |> ignore

    let getPlaylist () = String.concat "\n" [
        $"#EXTM3U"
        $"#EXT-X-ALLOW-CACHE:NO"
        $"#EXT-X-VERSION:1"
        $"#EXT-X-STREAM-INF:BANDWIDTH={bitsPerSecond},CODECS=\"mp4a.40.5\""
        $"chunklist.m3u8"
        $""
    ]

    let private color = "brown"

    let private inputParameters = $"-f lavfi -i \"anoisesrc=sample_rate=44100:color={color}\""
    let private outputParameters = $"""-f hls -hls_time 10 -hls_segment_type mpegts -hls_flags delete_segments+program_date_time+temp_file -c:a aac -ac 2 -b:a {bitsPerSecond} {Path.Combine(path, "chunklist.m3u8")}"""

    let private generatorProcess = lazy Process.Start(new ProcessStartInfo(
        $"ffmpeg",
        $"-re {inputParameters} {outputParameters}",
        RedirectStandardOutput = true))

    let init () = generatorProcess.Force()
