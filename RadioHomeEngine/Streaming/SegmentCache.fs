namespace RadioHomeEngine

open System

/// An in-memory cache of HLS media segments associated with a specific radio channel number.
type SegmentCache() =
    /// All currently cached segments and their data, in order from newest to oldest.
    let mutable cache = []

    /// The sequence number that will be used for the next segment.
    let mutable nextSequenceNumber = UInt128.One

    // Stores a decrypted audio segment to the cache, along with the metadata and its new sequence number.
    let add (segment: DownstreamSegment) =
        printfn "[SegmentCache] Adding %A -> %A (%A)" segment.mediaSequence nextSequenceNumber segment.dateTime
        let cacheItem = {
            segment with mediaSequence = nextSequenceNumber
        }
        cache <- cacheItem :: cache
        nextSequenceNumber <- nextSequenceNumber + UInt128.One

    /// Remove all but the ten most recent segments from the cache.
    let evictStale () =
        cache <- cache |> List.truncate 10

    /// Gets a segment's unencrypted audio data from the cache, if it exists, using its new sequence number.
    let tryGetData sequenceNumber =
        cache
        |> Seq.where (fun s -> s.mediaSequence = sequenceNumber)
        |> Seq.map (fun s -> s.data)
        |> Seq.tryHead

    member _.Add(segment) =
        let threshold =
            match Seq.tryHead cache with
            | Some segment -> segment.dateTime
            | None -> DateTimeOffset.MinValue

        if segment.dateTime > threshold then
            add segment
        else
            printfn $"[SegmentCache] Skipping segment {segment.mediaSequence} because {segment.dateTime} <= {threshold}"

        evictStale ()

    member _.GetSegments() =
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
