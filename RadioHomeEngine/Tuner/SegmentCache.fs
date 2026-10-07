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

// The SegmentCache module is in charge of fetching and storing segments from the currently tuned SiriusXM channel.
// This happens during a chunklist request, when new segments are found in the upstream chunklist.

// SegmentCache is not thread-safe and write operations should not occur in parallel.

module SegmentCache =
    type Segment = ChunklistParser.Segment

    type CacheItem = {
        cachedAt: DateTimeOffset
        data: byte array
        segment: Segment
    }

    type Encryption = Key1 | NoEncryption

    exception UnknownEncryptionException

    /// All currently cached segments and their data, in order from newest to oldest.
    let mutable private cache = []

    /// The sequence number that will be used for the next segment.
    let mutable private nextSequenceNumber = UInt128.One

    /// Decrypts segment data, if necessary.
    let private decryptSegmentAsync (segment: Segment) (data: byte array) cancellationToken = task {
        let encryption =
            match segment.key with
            | "METHOD=AES-128,URI=\"key/1\"" -> Key1
            | "NONE" -> NoEncryption
            | _ -> raise UnknownEncryptionException

        match encryption with
        | NoEncryption ->
            return data
        | Key1 ->
            use algorithm = Aes.Create()
            algorithm.Padding <- PaddingMode.PKCS7
            algorithm.Mode <- CipherMode.CBC
            algorithm.KeySize <- 128
            algorithm.BlockSize <- 128

            // SiriusXMClient retrieves and stores the encryption key as part of its `getPlaylistAsync` function.

            algorithm.Key <- SiriusXMClient.getKey ()

            algorithm.IV <-
                let iv = Array.zeroCreate 16
                BinaryPrimitives.WriteUInt128BigEndian(iv.AsSpan(), segment.mediaSequence)
                iv

            use outputStream = new MemoryStream()

            // Create cryptoStream inside another inner task,
            // establishing a scope that ensures it's flushed and disposed of before we return the result.

            do! task {
                use cryptoStream = new CryptoStream(outputStream, algorithm.CreateDecryptor(), CryptoStreamMode.Write)
                do! cryptoStream.WriteAsync(data, cancellationToken)
            }

            // All decrpyted data has now been written to outputStream.

            return outputStream.ToArray()
    }

    // Uses ffmpeg to recontainerize the decrpyted data into a `.ts` segment.
    // This assumes that the upstream segments use the `mp4a.40.2` codec.
    let private recontainerizeAsync (data: byte array) cancellationToken = task {
        use ffmpeg =
            new ProcessStartInfo(
                "ffmpeg",
                "-i - -f mpegts -c:a copy -",
                RedirectStandardInput = true,
                RedirectStandardOutput = true)
            |> Process.Start

        use buffer = new MemoryStream()

        let writeTask = ffmpeg.StandardInput.BaseStream.WriteAsync(data, cancellationToken)
        let readTask = ffmpeg.StandardOutput.BaseStream.CopyToAsync(buffer, cancellationToken)

        do! writeTask

        ffmpeg.StandardInput.BaseStream.Close()

        do! readTask
        do! ffmpeg.WaitForExitAsync(cancellationToken)

        return buffer.ToArray()
    }

    // Stores a decrypted audio segment to the cache, along with the original metadata and its new sequence number.
    let private add cacheItem =
        cache <- cacheItem :: cache
        nextSequenceNumber <- cacheItem.segment.mediaSequence + UInt128.One

    /// Downloads and caches a segment, storing it as the new most recent segment in the cache.
    let addAsync (segment: Segment) uri cancellationToken = task {
        let! encryptedData = SiriusXMClient.getFileAsync uri cancellationToken
        let! decryptedData = decryptSegmentAsync segment encryptedData.content cancellationToken

        let! segmentData = recontainerizeAsync decryptedData cancellationToken

        add {
            cachedAt = DateTimeOffset.UtcNow
            data = segmentData
            segment = {
                segment with
                    key = "NONE"
                    mediaSequence = nextSequenceNumber
                    path = $"/Proxy/chunk-{nextSequenceNumber}.ts"
            }
        }
    }

    /// Remove all but the ten most recent segments from the cache.
    let evictStale () =
        cache <- cache |> List.truncate 10

    /// Determines whether an upstream segment is older than a cached segment (possibly from a different SiriusXM channel) and should be skipped.
    let isOld (segment: Segment) =
        let newestKnown =
            cache
            |> Seq.choose (fun s -> s.segment.dateTime)
            |> Seq.tryHead

        match (newestKnown, segment.dateTime) with
        | (Some last, Some this) -> this <= last
        | _ -> false

    /// List the most recent segments available to the user agent, in order from oldest to newest.
    let list (count: int) =
        cache
        |> Seq.map (fun s -> s.segment)
        |> Seq.truncate 3
        |> Seq.rev

    /// Gets a segment's unencrypted audio data from the cache, if it exists, using its new sequence number.
    let tryGetData sequenceNumber =
        cache
        |> Seq.where (fun s -> s.segment.mediaSequence = sequenceNumber)
        |> Seq.map (fun s -> s.data)
        |> Seq.tryHead
