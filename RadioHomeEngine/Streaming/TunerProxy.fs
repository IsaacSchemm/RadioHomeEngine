namespace RadioHomeEngine

open System
open System.Buffers.Binary
open System.Diagnostics
open System.IO
open System.Runtime.Caching
open System.Security.Cryptography
open System.Text
open System.Text.RegularExpressions
open System.Threading
open System.Threading.Tasks

/// Takes the audio streams from SiriusXMClient, decrypts segments, and exposes them to the user.
module TunerProxy =
    // Since SiriusXMClient emulates a single user agent, this application can only stream one channel at a time.
    // Therefore, we read individual media segments from whichever channel is current whenever the chunklist is updated,
    // and expose a single playlist and chunklist based on what we have cached.
    // (This sacrifices the adaptive streaming capability of HLS for the sake of simplicity.)

    let mutable private currentChannel = None
    let mutable private currentChunklist = None

    // The playlist.m3u8 file must contain a bandwidth estimate for each chunklist.
    // We're only keeping one chunklist, so when we update it, let's update this value too.

    let mutable private bandwidth: int option = None

    // Prevent interference between different clients.
    // (Typically, there will be only one active listener to the stream, if any.)

    module private Lock =
        let flag = new SemaphoreSlim(1, 1)

        let doAsync (ct: CancellationToken) (f: unit -> Task<'T>) = task {
            do! flag.WaitAsync(ct)

            try
                return! f ()
            finally
                flag.Release() |> ignore
        }

    // Here we generate a media segment of 10 seconds of silence.
    // This will be used in place of any segment that is missing from the cache (e.g. too old).

    let private silentSegment = lazy task {
        let ffmpeg =
            new ProcessStartInfo(
                "ffmpeg",
                "-f lavfi -i anullsrc=cl=stereo:sample_rate=44100 -t 10 -c:a aac -f mpegts -",
                RedirectStandardOutput = true)
            |> Process.Start

        use buffer = new MemoryStream()

        let readTask = ffmpeg.StandardOutput.BaseStream.CopyToAsync(buffer)

        do! readTask
        do! ffmpeg.WaitForExitAsync()

        return buffer.ToArray()
    }

    /// The fallback chunklist URI to use when no channel is tuned.
    let private fallbackChunklist = new Uri($"http://localhost:{Config.port}/Noise/chunklist.m3u8")

    /// Returns the currently tuned channel number, if any.
    let getCurrentChannel() = Option.toNullable currentChannel

    /// Changes the currently tuned channel.
    /// This will fetch the stream information from SiriusXM and update this proxy's current chunklist pointer.
    let setCurrentChannelAsync channelNumber cancellationToken = Lock.doAsync cancellationToken (fun () -> task {
        currentChannel <- Some channelNumber
        currentChunklist <- None

        let! playlist = SiriusXMClient.tryGetPlaylistAsync channelNumber cancellationToken

        match playlist with
        | None -> ()
        | Some p ->
            // Download the playlist.m3u8 file.

            let playlistUri = new Uri(p.url)

            let! data = SiriusXMClient.getFileAsync playlistUri cancellationToken

            let text = Encoding.UTF8.GetString(data.content)

            // Find the estimated bandwidth value from the first chunklist and record it for use in our own `playlist.m3u8`.

            let matches = Regex.Matches(text, "^#EXT-X-STREAM-INF:.*BANDWIDTH=([0-9]+)")
            if matches.Count > 0 then
                bandwidth <- Some (Int32.Parse(matches.Item(0).Groups[1].Value))

            let lines = Utility.split '\n' text

            // Find the first chunklist and record its URL.

            currentChunklist <-
                text
                |> Utility.split '\n'
                |> Seq.where (fun line -> not (line.StartsWith('#')))
                |> Seq.map (fun line -> new Uri(playlistUri, line))
                |> Seq.tryHead
    })

    /// Untunes the currently tuned channel.
    let clearCurrentChannelAsync cancellationToken = Lock.doAsync cancellationToken (fun () -> task {
        currentChannel <- None
        currentChunklist <- None
    })

    /// Gets a list of currently and recently playing songs or programs on the currently tuned channel.
    let getCurrentChannelHistoryAsync cancellationToken = Lock.doAsync cancellationToken (fun () -> task {
        let! playlist =
            match currentChannel with
            | Some n -> SiriusXMClient.tryGetPlaylistAsync n cancellationToken
            | None -> task { return None }

        match playlist with
        | None -> return []
        | Some p -> return p.cuts
    })

    /// Gets a single segment's unencrypted audio data, using its new (client-facing) sequence number.
    /// If the segment is no longer cached, a segment that consists of ten seconds of silence will be returned instead.
    let getChunkAsync index sequenceNumber cancellationToken = task {
        match SegmentCache.tryGetData sequenceNumber with
        | Some s -> return s
        | None -> return! silentSegment.Value
    }

    /// Checks the newest upstream chunklist, downloads any new segments, and builds a client-facing `chunklist.m3u8`.
    let getChunklistAsync index cancellationToken = Lock.doAsync cancellationToken (fun () -> task {
        // Get the chunklist data.

        let chunklistUri =
            currentChunklist
            |> Option.defaultValue fallbackChunklist

        let! chunklist = SiriusXMClient.getFileAsync chunklistUri cancellationToken

        // Parse the file and remove any segments that are already cached,
        // or that are known to be older than an already-cached segment.

        let chunks =
            chunklist.content
            |> Encoding.UTF8.GetString
            |> ChunklistParser.parse
            |> Seq.where (not << SegmentCache.exists)
            |> Seq.where (not << SegmentCache.isOld)

        // Only cache the last three segments from the resulting list.

        let newChunks =
            chunks
            |> Seq.rev
            |> Seq.truncate 3
            |> Seq.rev

        for chunk in newChunks do
            let uri = new Uri(chunklistUri, chunk.path)
            do! SegmentCache.addAsync chunk uri cancellationToken

        // Remove old segments from the cache as needed.

        SegmentCache.evictStale ()

        let content = String.concat "\n" [
            // Build the chunklist.
            // Only include the three most recent segments.

            ChunklistParser.write [
                for cacheItem in SegmentCache.getRecent () |> Seq.truncate 3 |> Seq.rev do
                    cacheItem.DownstreamSegment
            ]

            // If there is no currently tuned channel, we don't expect any more segments, so end the stream here.
            // In the future, this might be changed to either hang or append silent segments.
        ]

        return content
    })

    /// Builds a client-facing `playlist.m3u8`.
    let getPlaylist () = String.concat "\n" [
        "#EXTM3U"
        "#EXT-X-ALLOW-CACHE:NO"
        "#EXT-X-VERSION:1"

        String.concat "" [
            "#EXT-X-STREAM-INF:"

            match bandwidth with
            | Some bps -> $"BANDWIDTH={bps},"
            | None -> ()

            "CODECS=\"mp4a.40.2\""
        ]

        "chunklist.m3u8"
    ]
