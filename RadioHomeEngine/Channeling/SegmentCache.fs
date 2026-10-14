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
open Microsoft.Extensions.Hosting

// The SegmentCache module is in charge of fetching and storing segments from the currently tuned SiriusXM channel.
// This happens during a chunklist request, when new segments are found in the upstream chunklist.

module SegmentCache =
    type Encryption = Key1 | NoEncryption

    exception UnknownEncryptionException

    /// All currently cached segments and their data, in order from newest to oldest.
    let mutable private cache = []

    /// The sequence number that will be used for the next segment.
    let mutable private nextSequenceNumber = UInt128.One

    /// The last date and time at which the cache was accessed. Cache population is paused when not in use.
    let mutable private lastRequested = DateTimeOffset.UtcNow

    // Stores a decrypted audio segment to the cache, along with the metadata and its new sequence number.
    let private add (segment: DownstreamSegment) =
        let cacheItem = {
            segment with mediaSequence = nextSequenceNumber
        }
        cache <- cacheItem :: cache
        nextSequenceNumber <- nextSequenceNumber + UInt128.One

    /// Remove all but the ten most recent segments from the cache.
    let private evictStale () =
        cache <- cache |> List.truncate 10

    let private isInUse () =
        DateTimeOffset.UtcNow - lastRequested < TimeSpan.FromMinutes(1.0)

    let private readFrom (segmentBuffer: SegmentBuffer) =
        // Look for segments that have a timestamp at least [segment-length / 2] seconds newer than the newest cached segment.
        // Never take more than the three most recent segments, though.

        let mutable threshold =
            match Seq.tryHead cache with
            | Some segment -> segment.dateTime + TimeSpan.FromSeconds(float segment.duration / 2.0)
            | None -> DateTimeOffset.MinValue

        // Add any newly generated chunks.

        for segment in segmentBuffer.ReadAll() do
            if segment.dateTime > threshold then
                add segment
                threshold <- segment.dateTime

    /// List the most recent segments available to the user agent, in order from oldest to newest.
    let list (count: int) =
        lastRequested <- DateTimeOffset.UtcNow

        cache
        |> Seq.truncate count
        |> Seq.rev

    /// Gets a segment's unencrypted audio data from the cache, if it exists, using its new sequence number.
    let tryGetData sequenceNumber =
        lastRequested <- DateTimeOffset.UtcNow

        cache
        |> Seq.where (fun s -> s.mediaSequence = sequenceNumber)
        |> Seq.map (fun s -> s.data)
        |> Seq.tryHead

    let getPlaylist () = String.concat "\n" [
        "#EXTM3U"
        "#EXT-X-ALLOW-CACHE:NO"
        "#EXT-X-VERSION:1"

        String.concat "" [
            "#EXT-X-STREAM-INF:"
            "CODECS=\"mp4a.40.2\""
        ]

        "chunklist.m3u8"
    ]

    let getChunklist () = String.concat "\n" [
        "#EXTM3U"

        match cache with
        | [] -> ()
        | segment :: _ -> $"#EXT-X-TARGETDURATION:{int (round segment.duration)}"

        "#EXT-X-VERSION:1"
        "#EXT-X-ALLOW-CACHE:NO"

        let segments = cache |> List.truncate 3 |> List.rev

        match segments with
        | [] -> ()
        | segment :: _ -> $"#EXT-X-MEDIA-SEQUENCE:{segment.mediaSequence}"

        let mutable encryption = Clear

        for segment in segments do
            $"#EXT-X-PROGRAM-DATE-TIME:{segment.dateTime:o}"
            $"#EXTINF:{segment.duration},"
            $"/Stream/chunk-{segment.mediaSequence}.ts"
    ]

    let getData sequenceNumber =
        match tryGetData sequenceNumber with
        | Some s -> s
        | _ -> Silence.getSegment ()

    type Service() =
        inherit BackgroundService()

        override _.ExecuteAsync(cancellationToken) = task {
            while not cancellationToken.IsCancellationRequested do
                if isInUse () then
                    use segmentBuffer = new SegmentBuffer((fun () -> new NoiseSource()), cancellationToken)
                    while isInUse () do
                        readFrom segmentBuffer
                        evictStale ()

                        do! Task.Delay(TimeSpan.FromSeconds(1.0), cancellationToken)

                do! Task.Delay(TimeSpan.FromSeconds(5.0), cancellationToken)
        }
