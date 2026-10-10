namespace RadioHomeEngine

open System

module ChunklistParser =
    type Segment = {
        /// The value of the EXT-X-KEY tag that applies to this segment. May be inherited from the previous segment.
        key: string

        /// Any miscellaneous tags that apply to the entire chunklist.
        headerTags: string list

        /// The absolute date and time of the start of this media segment.
        dateTime: DateTimeOffset option

        /// The sequence number of this segment.
        mediaSequence: UInt128

        /// The duration of this chunk, in seconds.
        duration: decimal option

        /// The range of bytes in the file that constitute this chunk.
        byteRange: string option

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

        let mutable dateTime = None
        let mutable mediaSequence = zero
        let mutable duration = None
        let mutable byteRange = None

        for line in Utility.split '\n' text do
            match line with
            | Tag ("EXT-X-KEY", value) ->
                // This tag usually applies to the whole chunklist, but theoretically, it can be changed between chunks.
                key <- value

            | Tag ("EXT-X-MEDIA-SEQUENCE", UInt128 value) ->
                mediaSequence <- value

            | Tag ("EXT-X-PROGRAM-DATE-TIME", DateTimeOffset value) ->
                dateTime <- Some value

            | Tag ("EXTINF", CommaSeparated (Decimal value :: _)) ->
                duration <- Some value

            | Tag ("EXT-X-BYTERANGE", value) ->
                byteRange <- Some value

            | Tag _ ->
                // All other tags are associated with the entire chunklist.
                headerTags <- List.rev (line :: headerTags)

            | _ when not (line.StartsWith('#')) ->
                // This line is not a tag, so it represents an actual chunk.
                {
                    key = key
                    headerTags = headerTags
                    dateTime = dateTime
                    mediaSequence = mediaSequence
                    duration = duration
                    byteRange = byteRange
                    path = line
                }

                dateTime <-
                    match dateTime, duration with
                    | Some dt, Some sec -> Some (dt + TimeSpan.FromSeconds(float sec))
                    | _ -> None

                mediaSequence <- mediaSequence + one
                duration <- None
                byteRange <- None
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

            match segment.dateTime with
            | Some dateTime -> yield $"#EXT-X-PROGRAM-DATE-TIME:{dateTime:o}"
            | None -> ()

            match segment.duration with
            | Some duration -> yield $"#EXTINF:{duration},"
            | None -> ()

            match segment.byteRange with
            | Some byteRange -> yield $"#EXT-X-BYTERANGE:{byteRange}"
            | None -> ()

            yield segment.path
    ]
