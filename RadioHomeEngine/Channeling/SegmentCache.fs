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
    type Segment = UpstreamSegment

    type CacheItem = {
        data: byte array
        segment: Segment
    }

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
            data = segment.data
            segment = {
                encryption = Clear
                dateTime = Some segment.dateTime
                mediaSequence = nextSequenceNumber
                duration = Some segment.duration
                path = $"/Proxy/chunk-{nextSequenceNumber}.ts"
            }
        }
        cache <- cacheItem :: cache
        nextSequenceNumber <- cacheItem.segment.mediaSequence + UInt128.One

    /// Remove all but the ten most recent segments from the cache.
    let private evictStale () =
        cache <- cache |> List.truncate 10

    let private isInUse () =
        DateTimeOffset.UtcNow - lastRequested < TimeSpan.FromSeconds(30.0)

    /// List the most recent segments available to the user agent, in order from oldest to newest.
    let list (count: int) =
        lastRequested <- DateTimeOffset.UtcNow

        cache
        |> Seq.map (fun s -> s.segment)
        |> Seq.truncate count
        |> Seq.rev

    /// Gets a segment's unencrypted audio data from the cache, if it exists, using its new sequence number.
    let tryGetData sequenceNumber =
        lastRequested <- DateTimeOffset.UtcNow

        cache
        |> Seq.where (fun s -> s.segment.mediaSequence = sequenceNumber)
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

    let getChunklist () =
        ChunklistParser.write (list 3)

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
                        // Look for segments that have a timestamp at least [segment-length / 2] seconds newer than the newest cached segment.
                        // Never take more than the three most recent segments, though.

                        let newestSegment = Seq.tryHead cache

                        let threshold =
                            let dateTime =
                                newestSegment
                                |> Option.bind (fun cacheItem -> cacheItem.segment.dateTime)
                                |> Option.defaultValue DateTimeOffset.MinValue
                            let duration =
                                newestSegment
                                |> Option.bind (fun cacheItem -> cacheItem.segment.duration)
                                |> Option.defaultValue 0.0

                            dateTime + TimeSpan.FromSeconds(float duration / 2.0)

                        // Look for newly generated chunks.

                        let mutable more = true
                        while more do
                            match segmentBuffer.TryRead() with
                            | Some segment ->
                                if segment.dateTime > threshold then
                                    add segment
                            | None ->
                                more <- false

                        // Remove old segments from the cache as needed.

                        evictStale ()

                        do! Task.Delay(TimeSpan.FromSeconds(1.0), cancellationToken)

                do! Task.Delay(TimeSpan.FromSeconds(5.0), cancellationToken)
        }
