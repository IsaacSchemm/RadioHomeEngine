namespace RadioHomeEngine

open System

/// Parses a chunklist.m3u8 file from a remote server.
module ChunklistParser =
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
        let mutable encryption = Clear
        let mutable dateTime = None
        let mutable mediaSequence = zero
        let mutable duration = None
        let mutable byteRange = None

        for line in Utility.split '\n' text do
            match line with
            | Tag ("EXT-X-KEY", CommaSeparated attributes) ->
                // This tag usually applies to the whole chunklist, but theoretically, it can be changed between chunks.
                encryption <-
                    match List.sort attributes with
                    | [HLSAttribute ("METHOD", "NONE")] ->
                        Clear
                    | [HLSAttribute ("METHOD", "AES-128"); HLSAttribute ("URI", HLSQuotedString uri)] ->
                        Aes128 uri
                    | _ ->
                        Unknown

            | Tag ("EXT-X-MEDIA-SEQUENCE", UInt128 value) ->
                mediaSequence <- value

            | Tag ("EXT-X-PROGRAM-DATE-TIME", DateTimeOffset value) ->
                dateTime <- Some value

            | Tag ("EXTINF", CommaSeparated (Float64 value :: _)) ->
                duration <- Some value

            | Tag ("EXT-X-BYTERANGE", value) ->
                raise (new NotImplementedException())

            | Tag _ -> ()

            | _ when not (line.StartsWith('#')) ->
                // This line is not a tag, so it represents an actual chunk.
                {
                    encryption = encryption
                    dateTime = dateTime
                    mediaSequence = mediaSequence
                    duration = duration
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
