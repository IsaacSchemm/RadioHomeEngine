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
    type UpstreamSegment = ChunklistParser.Segment

    type Encryption = Key1 | NoEncryption

    exception UnknownEncryptionException

    /// All currently cached segments, in order from newest to oldest.
    let mutable private segments = []

    /// The sequence number that will be used for the next segment.
    let mutable private nextSequenceNumber = UInt128.One

    /// Downloads and caches a segment, storing it as the new most recent segment in the cache.
    let addAsync (segment: UpstreamSegment) uri cancellationToken = task {
        // Get the original segment and check whether it's encrypted.

        let! encryptedData = SiriusXMClient.getFileAsync uri cancellationToken

        let encryption =
            match segment.key with
            | "METHOD=AES-128,URI=\"key/1\"" -> Key1
            | "NONE" -> NoEncryption
            | _ -> raise UnknownEncryptionException

        // Get the unencrypted segment data.
        // This happens inside an inner task so we can avoid having to assign to a mutable variable.

        let! decryptedData = task {
            match encryption with
            | NoEncryption ->
                return encryptedData.content
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
                    do! cryptoStream.WriteAsync(encryptedData.content)
                }

                // All decrpyted data has now been written to outputStream.

                return outputStream.ToArray()
        }

        // Use ffmpeg to recontainerize the decrpyted data into a `.ts` segment.
        // This assumes that the upstream segments use the `mp4a.40.2` codec.

        use ffmpeg =
            new ProcessStartInfo(
                "ffmpeg",
                "-i - -f mpegts -c:a copy -",
                RedirectStandardInput = true,
                RedirectStandardOutput = true)
            |> Process.Start

        let! segmentData = task {
            use buffer = new MemoryStream()

            let writeTask = ffmpeg.StandardInput.BaseStream.WriteAsync(decryptedData)
            let readTask = ffmpeg.StandardOutput.BaseStream.CopyToAsync(buffer)

            do! writeTask

            ffmpeg.StandardInput.BaseStream.Close()

            do! readTask
            do! ffmpeg.WaitForExitAsync()

            return buffer.ToArray()
        }

        // Store both the original metadata (as it existed in the original stream)
        // and the new metadata (as it exists in this application's output stream),
        // along with the actual data.

        let newSegment = {|
            original = segment
            proxied = {
                segment with
                    key = "NONE"
                    mediaSequence = nextSequenceNumber
                    path = $"chunk-{nextSequenceNumber}.ts"
            }
            cachedAt = DateTimeOffset.UtcNow
            data = segmentData
        |}

        segments <- newSegment :: segments

        nextSequenceNumber <- newSegment.proxied.mediaSequence + UInt128.One
    }

    /// Returns data for the most recent segments in the cache.
    let getRecent () =
        segments
        |> Seq.truncate 5

    /// Determines whether an upstream segment is older than a cached segment (possibly from a different SiriusXM channel) and should be skipped.
    let isOld (segment: UpstreamSegment) =
        let newestKnown =
            segments
            |> Seq.choose (fun s -> s.original.dateTime)
            |> Seq.tryHead

        match (newestKnown, segment.dateTime) with
        | (Some last, Some this) -> this <= last
        | _ -> false

    /// Check whether an upstream segment exists in decrypted form in the cache.
    let exists originalSegment =
        segments
        |> Seq.exists (fun s -> s.original = originalSegment)

    /// Gets a segment's unencrypted audio data from the cache, if it exists.
    let tryGetData sequenceNumber =
        segments
        |> Seq.where (fun s -> s.proxied.mediaSequence = sequenceNumber)
        |> Seq.map (fun s -> s.data)
        |> Seq.tryHead

    /// Remove all but the ten most recent segments from the cache.
    let evictStale () =
        segments <- segments |> List.truncate 10
