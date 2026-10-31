namespace RadioHomeEngine

open System
open System.Diagnostics
open System.IO

/// An in-memory cache of HLS media segments associated with a specific radio channel number.
type SegmentCache() =
    /// All currently cached segments and their data, in order from newest to oldest.
    let mutable cache = []

    /// The sequence number that will be used for the next segment.
    let mutable nextSequenceNumber = UInt128.One

    /// The bitrate shown alongside the chunklist entry in the playlist file. Should not change once segments exist.
    let mutable nominalBitrate = 327680

    // Stores a decrypted audio segment to the cache, along with the metadata and its new sequence number.
    let add (segment: DownstreamSegment) =
        printfn "[SegmentCache] Adding %A -> %A (%A)" segment.mediaSequence nextSequenceNumber segment.dateTime
        if cache = [] then
            nominalBitrate <- segment.BitsPerSecond
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

    /// Wraps a task that will generate a single HLS segment consisting of ten seconds of silence.
    let fallbackSegmentTask = lazy task {
        let ffmpeg =
            new ProcessStartInfo(
                "ffmpeg",
                $"-nostats -hide_banner -loglevel warning -f lavfi -i anullsrc=cl=stereo:sample_rate=44100 -t 10 -c:a aac -b:a 65536 -f mpegts -",
                RedirectStandardOutput = true)
            |> Process.Start

        use buffer = new MemoryStream()

        let readTask = ffmpeg.StandardOutput.BaseStream.CopyToAsync(buffer)

        do! readTask
        do! ffmpeg.WaitForExitAsync()

        return buffer.ToArray()
    }

    member _.NominalBitrate =
        nominalBitrate

    member _.Add(segment: DownstreamSegment) =
        let threshold =
            match Seq.tryHead cache with
            | Some segment -> segment.dateTime
            | None -> DateTimeOffset.MinValue

        if segment.dateTime > threshold then
            add segment
        else if segment.important then
            printfn $"[SegmentCache] Adding important segment ({segment.dateTime} < {threshold})"
            add { segment with dateTime = threshold.AddSeconds(0.1) }
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
            return! fallbackSegmentTask.Value
    }
