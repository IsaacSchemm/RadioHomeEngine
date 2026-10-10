namespace RadioHomeEngine

open System

module ChunklistNormalizer =
    type Segment = ChunklistParser.Segment

    type KnownStream(uri: Uri, offset: TimeSpan) =
        member _.Uri = uri
        member _.Offset = offset

        member val LastSeen = DateTimeOffset.UtcNow

    let private knownStreams = new ResizeArray<KnownStream>()

    let calculateOffset (segments: Segment list) =
        let upstreamDateTime =
            segments
            |> Seq.choose (fun s -> s.dateTime)
            |> Seq.max

        DateTimeOffset.UtcNow - upstreamDateTime

    let getOffset (uri: Uri) (segments: Segment list) = lock knownStreams (fun () -> Seq.head (seq {
        for k in List.ofSeq knownStreams do
            if DateTimeOffset.UtcNow - k.LastSeen > TimeSpan.FromMinutes(1L) then
                knownStreams.Remove(k) |> ignore

        for k in knownStreams do
            if k.Uri = uri then
                yield k.Offset

        let offset = calculateOffset segments
        knownStreams.Add(new KnownStream(uri, offset))
        yield offset
    }))

    let normalize uri segments = [
        let offset = getOffset uri segments

        for segment in segments do {
            segment with
                dateTime = segment.dateTime |> Option.map (fun dt -> dt + offset)
        }
    ]
