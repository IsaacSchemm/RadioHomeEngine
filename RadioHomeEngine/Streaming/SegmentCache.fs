namespace RadioHomeEngine

open System

type SegmentCache() =
    /// All currently cached segments and their data, in order from newest to oldest.
    let mutable cache = []

    /// The sequence number that will be used for the next segment.
    let mutable nextSequenceNumber = UInt128.One

    /// The last date and time at which the cache was accessed. Cache population is paused when not in use.
    let mutable lastRequested = DateTimeOffset.UtcNow

    // Stores a decrypted audio segment to the cache, along with the metadata and its new sequence number.
    let add (segment: DownstreamSegment) =
        let cacheItem = {
            segment with mediaSequence = nextSequenceNumber
        }
        cache <- cacheItem :: cache
        nextSequenceNumber <- nextSequenceNumber + UInt128.One

    /// Remove all but the ten most recent segments from the cache.
    let evictStale () =
        cache <- cache |> List.truncate 10

    let isInUse () =
        DateTimeOffset.UtcNow - lastRequested < TimeSpan.FromMinutes(1.0)

    let readFrom (source: ILocalSegmentSource) =
        // Look for segments that have a timestamp at least [segment-length / 2] seconds newer than the newest cached segment.
        // Never take more than the three most recent segments, though.

        let mutable threshold =
            match Seq.tryHead cache with
            | Some segment -> segment.dateTime + TimeSpan.FromSeconds(float segment.duration / 2.0)
            | None -> DateTimeOffset.MinValue

        // Add any newly generated chunks.

        let mutable moreNow = true
        let mutable moreLater = true

        while moreNow do
            match source.TryReadNext() with
            | NextSegment segment ->
                if segment.dateTime > threshold then
                    add segment
                    threshold <- segment.dateTime
            | NoMoreSegments ->
                moreNow <- false
                moreLater <- false
            | WaitingForMoreSegments ->
                moreNow <- false

        moreLater

    /// Gets a segment's unencrypted audio data from the cache, if it exists, using its new sequence number.
    let tryGetData sequenceNumber =
        cache
        |> Seq.where (fun s -> s.mediaSequence = sequenceNumber)
        |> Seq.map (fun s -> s.data)
        |> Seq.tryHead

    member _.GetSegments() =
        lastRequested <- DateTimeOffset.UtcNow
        SegmentCacheActivityIndicator.notify ()

        cache
        |> List.truncate 3
        |> List.rev

    member _.GetSegmentDataAsync(sequenceNumber) = task {
        match tryGetData sequenceNumber with
        | Some s ->
            return s
        | _ ->
            return! Silence.segmentTask.Value
    }

    member _.IsInUse() =
        isInUse ()

    member _.ReadFrom(buffer) =
        readFrom buffer

    member _.EvictStale() =
        evictStale ()
