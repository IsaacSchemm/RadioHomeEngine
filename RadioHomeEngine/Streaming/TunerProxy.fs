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
    type Encryption = Key1 | NoEncryption

    exception UnknownEncryptionException

    // This proxy carries the concept of a "current channel".
    // Since SiriusXMClient emulates a single user agent, this application can only stream one channel at a time.
    // Therefore, we read individual media segments from whichever channel is current whenever the chunklist is updated,
    // and expose a single playlist and chunklist based on what we have cached.
    // (This sacrifices the adaptive streaming capability of HLS for the sake of simplicity.)

    let mutable private currentChannel = None
    let mutable private currentChunklist = None

    // The playlist.m3u8 file contains a bandwidth estimate for each chunklist.
    // We're only keeping one chunklist, so when we update it, let's update this value too.

    let mutable private bandwidth = 281600

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

    let getCurrentChannel() = Option.toNullable currentChannel

    let setCurrentChannelAsync channelNumber cancellationToken = Lock.doAsync cancellationToken (fun () -> task {
        currentChannel <- Some channelNumber
        currentChunklist <- None

        let! playlist = SiriusXMClient.tryGetPlaylistAsync channelNumber cancellationToken

        match playlist with
        | None -> ()
        | Some p ->
            let playlistUri = new Uri(p.url)

            let! data = SiriusXMClient.getFileAsync playlistUri cancellationToken

            let text = Encoding.UTF8.GetString(data.content)

            let matches = Regex.Matches(text, "^#EXT-X-STREAM-INF:.*BANDWIDTH=([0-9]+)")
            if matches.Count > 0 then
                bandwidth <- matches.Item(0).Groups[1].Value |> Int32.Parse

            let lines = Utility.split '\n' text

            currentChunklist <- Seq.tryHead (seq {
                let mutable i = 0
                for line in lines do
                    if not (line.StartsWith('#')) then
                        yield new Uri(playlistUri, line)
            })
    })

    let clearCurrentChannelAsync cancellationToken = Lock.doAsync cancellationToken (fun () -> task {
        currentChannel <- None
        currentChunklist <- None
    })

    let getCurrentChannelHistoryAsync cancellationToken = Lock.doAsync cancellationToken (fun () -> task {
        let! playlist =
            match currentChannel with
            | Some n -> SiriusXMClient.tryGetPlaylistAsync n cancellationToken
            | None -> task { return None }

        match playlist with
        | None -> return []
        | Some p -> return p.cuts
    })

    let getPlaylistAsync cancellationToken = task {
        return String.concat "\n" [
            "#EXTM3U"
            "#EXT-X-ALLOW-CACHE:NO"
            "#EXT-X-VERSION:1"
            $"#EXT-X-STREAM-INF:PROGRAM-ID=1,BANDWIDTH={bandwidth},CODECS=\"mp4a.40.2\""
            "chunklist-0-0.m3u8"
        ]
    }

    module private SegmentCache =
        type Segment = {
            original: ChunklistParser.Segment
            proxied: ChunklistParser.Segment
            data: byte array
        }

        let mutable segments = []

        let tryFindByOriginal segment =
            segments
            |> Seq.where (fun s -> s.original = segment)
            |> Seq.tryHead

        let addAsync (segment: ChunklistParser.Segment) uri cancellationToken = task {
            let prev =
                match segments with
                | [] ->
                    UInt128.Zero
                | _::_ ->
                    segments
                    |> Seq.map (fun s -> s.proxied.mediaSequence)
                    |> Seq.max

            let next = prev + UInt128.One

            let! encryptedData = SiriusXMClient.getFileAsync uri cancellationToken

            let encryption =
                match segment.key with
                | "METHOD=AES-128,URI=\"key/1\"" -> Key1
                | "NONE" -> NoEncryption
                | _ -> raise UnknownEncryptionException

            let! data = task {
                match encryption with
                | NoEncryption ->
                    return encryptedData.content
                | Key1 ->
                    use algorithm = Aes.Create()
                    algorithm.Padding <- PaddingMode.PKCS7
                    algorithm.Mode <- CipherMode.CBC
                    algorithm.KeySize <- 128
                    algorithm.BlockSize <- 128

                    algorithm.Key <- SiriusXMClient.getKey ()

                    algorithm.IV <-
                        let iv = Array.zeroCreate 16
                        BinaryPrimitives.WriteUInt128BigEndian(iv.AsSpan(), segment.mediaSequence)
                        iv

                    use outputStream = new MemoryStream()

                    do! task {
                        use cryptoStream = new CryptoStream(outputStream, algorithm.CreateDecryptor(), CryptoStreamMode.Write)
                        do! cryptoStream.WriteAsync(encryptedData.content)
                    }

                    return outputStream.ToArray()
            }

            let ffmpeg =
                new ProcessStartInfo(
                    "ffmpeg",
                    "-i - -f mpegts -c:a copy -",
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true)
                |> Process.Start

            let! decrypted_data = task {
                use buffer = new MemoryStream()

                let writeTask = ffmpeg.StandardInput.BaseStream.WriteAsync(data)
                let readTask = ffmpeg.StandardOutput.BaseStream.CopyToAsync(buffer)

                do! writeTask

                ffmpeg.StandardInput.BaseStream.Close()

                do! readTask
                do! ffmpeg.WaitForExitAsync()

                return buffer.ToArray()
            }

            segments <- {
                original = segment
                proxied = {
                    segment with
                        key = "NONE"
                        mediaSequence = next
                        path = $"chunk-0-0-{next}.ts"
                }
                data = decrypted_data
            } :: segments
        }

    let private tryParseDateTime (segment: ChunklistParser.Segment) = Seq.tryHead (seq {
        let prefix = "#EXT-X-PROGRAM-DATE-TIME:"
        for headerTag in segment.segmentTags do
            if headerTag.StartsWith(prefix) then
                match headerTag.Substring(prefix.Length) |> DateTimeOffset.TryParse with
                | true, dt -> yield dt
                | false, _ -> ()
    })

    let getChunklistAsync index cancellationToken = Lock.doAsync cancellationToken (fun () -> task {
        match currentChunklist with
        | None -> ()
        | Some chunklistUri ->
            let! data = SiriusXMClient.getFileAsync chunklistUri cancellationToken

            let chunks =
                data.content
                |> Encoding.UTF8.GetString
                |> ChunklistParser.parse

            let relevantChunks = [
                let lastDate =
                    SegmentCache.segments
                    |> Seq.map (fun s -> s.original)
                    |> Seq.choose tryParseDateTime
                    |> Seq.tryHead

                for chunk in chunks do
                    match (lastDate, tryParseDateTime chunk) with
                    | (Some last, Some this) when this <= last -> ()
                    | _ -> yield chunk
            ]

            for x in relevantChunks do
                match SegmentCache.tryFindByOriginal x with
                | Some _ -> ()
                | None ->
                    let uri = new Uri(chunklistUri, x.path)
                    do! SegmentCache.addAsync x uri cancellationToken

        let content = String.concat "\n" [
            ChunklistParser.write [
                for x in SegmentCache.segments |> Seq.truncate 3 |> Seq.rev do
                    yield x.proxied
            ]

            if currentChunklist = None then
                "#EXT-X-ENDLIST"
        ]

        return content
    })

    // Gets a single segment.
    // TODO: this probably doesn't need to be behind the semaphore?

    let getChunkAsync index sequenceNumber cancellationToken = Lock.doAsync cancellationToken (fun () -> task {
        let data =
            SegmentCache.segments
            |> Seq.where (fun s -> s.proxied.mediaSequence = sequenceNumber)
            |> Seq.map (fun s -> s.data)
            |> Seq.head

        return data
    })
