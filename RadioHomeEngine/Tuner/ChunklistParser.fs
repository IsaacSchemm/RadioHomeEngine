namespace RadioHomeEngine

open System

module ChunklistParser =
    type Segment = {
        /// The value of the EXT-X-KEY tag that applies to this segment. May be inherited from the previous segment.
        key: string

        /// Any miscellaneous tags that apply to the entire chunklist.
        headerTags: string list

        /// The value of the EXT-X-PROGRAM-DATE-TIME tag for this segment.
        dateTime: DateTimeOffset

        /// The value of the EXT-X-MEDIA-SEQUENCE tag for this segment.
        mediaSequence: UInt128

        /// Any miscellaneous tags for this segment.
        segmentTags: string list

        /// The path to the chunk, relative to the chunklist.
        path: string
    }

    let zero = UInt128.Zero
    let one = UInt128.One

    /// An active pattern that parses a string as an M3U directive.
    let (|Tag|_|) (str: string) =
        match str.StartsWith('#'), str.IndexOf(':') with
        | false, _
        | true, -1 -> None
        | true, index ->
            let name = str.Substring(1, index - 1)
            let value = str.Substring(index + 1)
            Some (name, value)

    let parse text = [
        let mutable key = "NONE"
        let mutable headerTags = []
        let mutable segmentTags = []

        let mutable dateTime = None
        let mutable mediaSequence = zero

        for line in Utility.split '\n' text do
            match line with
            | Tag ("EXT-X-KEY", value) ->
                // This tag usually applies to the whole chunklist, but theoretically, it can be changed between chunks.
                key <- value

            | Tag ("EXT-X-MEDIA-SEQUENCE", UInt128 value) ->
                mediaSequence <- value

            | Tag ("EXT-X-PROGRAM-DATE-TIME", DateTimeOffset value) ->
                dateTime <- Some value

            | Tag ("EXTINF", _)
            | Tag ("EXT-X-BYTERANGE", _) ->
                // These tags are associated with a specific segment.
                segmentTags <- List.rev (line :: segmentTags)

            | Tag _ ->
                // All other tags are associated with the entire chunklist.
                headerTags <- List.rev (line :: headerTags)

            | _ when not (line.StartsWith('#')) ->
                // This line is not a tag, so it represents an actual chunk.
                match dateTime with
                | None -> ()
                | Some dt -> {
                    key = key
                    headerTags = headerTags
                    dateTime = dt
                    mediaSequence = mediaSequence
                    segmentTags = segmentTags
                    path = line
                }

                segmentTags <- []
                dateTime <- None
                mediaSequence <- mediaSequence + one
            | _ -> ()
    ]

    let write segments = String.concat "\n" [
        yield "#EXTM3U"

        match Seq.tryHead segments with
        | None -> ()
        | Some segment ->
            yield! segment.headerTags

            if segment.mediaSequence <> zero then
                yield $"#EXT-X-MEDIA-SEQUENCE:{segment.mediaSequence}"

        let mutable lastKey = "NONE"

        for segment in segments do
            if segment.key <> lastKey then
                yield $"EXT-X-KEY:{segment.key}"
                lastKey <- segment.key

            yield $"#EXT-X-PROGRAM-DATE-TIME:{segment.dateTime:o}"

            yield! segment.segmentTags

            yield segment.path
    ]
