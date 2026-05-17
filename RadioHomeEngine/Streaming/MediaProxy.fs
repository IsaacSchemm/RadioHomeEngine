namespace RadioHomeEngine

open System
open System.Buffers.Binary
open System.Diagnostics
open System.IO
open System.Runtime.Caching
open System.Security.Cryptography
open System.Text
open System.Threading.Tasks

module MediaProxy =
    type Chunklist = {
        channelId: string
        index: int
        uri: Uri
    }

    type Encryption = Key1 | NoEncryption

    type Chunk = {
        chunklist: Chunklist
        uri: Uri
        sequenceNumber: UInt128
        encryption: Encryption
    }

    exception MediaNotCachedException
    exception UnknownEncryptionException

    // To avoid having long, redundant URLs for media content, RadioHomeEngine will
    // expose chunklists (renditions) and chunks (segments) with custom IDs
    // that consist of:
    //
    // * the channel ID
    // * the index (starting at 0) of the chunklist
    // * the chunk's sequence number (which is also used in decryption)
    //
    // When handling a client request for a chunklist or a chunk, this
    // application can't know everything it needs to know from the URL alone;
    // it needs some metadata from the parent level:
    //
    // * To serve the chunklist, we need to know its original URI.
    //
    // * To serve a chunk, we need to know its original URI, and whether it
    //   uses an encryption key or not (although all SiriusXM streams do).
    //
    // To allow us to use these simplified identifiers in our chunklist and
    // chunk URLs, we cache the rest of the info we need when we fetch the
    // original list, and if the info is missing from the cache, we re-fetch
    // the original list and then try again.
    //
    // This metadata will remain in the cache until it hasn't been used for
    // five minutes. (The actual raw media data is not cached.)

    module Cache =
        let cache = MemoryCache.Default
        let cacheKey = Guid.NewGuid()

        let store key item =
            let _ = cache.Add(
                $"{cacheKey}-{key}",
                item,
                new CacheItemPolicy(SlidingExpiration = TimeSpan.FromMinutes(5.0)))
            ()

        let tryRetrieve key =
            match cache.Get($"{cacheKey}-{key}") with
            | :? 'T as item -> Some item
            | _ -> None

        type RetrievalOptions = {
            key: string
            onRetryAsync: unit -> Task
        }

        let tryRetrieveWithRetryAsync retrieval = task {
            match tryRetrieve retrieval.key with
            | Some item ->
                return item
            | None ->
                let! _ = retrieval.onRetryAsync ()
                return tryRetrieve retrieval.key |> Option.defaultWith (fun () -> raise MediaNotCachedException)
        }

    let getPlaylistAsync id cancellationToken = task {
        let! channels = SiriusXMClient.getChannelsAsync cancellationToken

        let channel =
            channels
            |> Seq.where (fun c -> c.channelId = id)
            |> Seq.head

        let! playlist = SiriusXMClient.getPlaylistAsync channel.channelGuid channel.channelId cancellationToken

        let playlistUri = new Uri(playlist.url)

        let! data = SiriusXMClient.getFileAsync playlistUri cancellationToken

        let lines =
            data.content
            |> Encoding.UTF8.GetString
            |> Utility.split '\n'

        let content = String.concat "\n" [
            let mutable i = 0
            for line in lines do
                if line.StartsWith('#') then
                    line
                else
                    Cache.store $"{id}-{i}" {
                        channelId = id
                        index = i
                        uri = new Uri(playlistUri, line)
                    }
                    $"chunklist-{id}-{i}.m3u8"
                    i <- i + 1
        ]

        return content
    }

    let getChunklistAsync id index cancellationToken = task {
        let! (chunklist: Chunklist) = Cache.tryRetrieveWithRetryAsync {
            key = $"{id}-{index}"
            onRetryAsync = fun () -> getPlaylistAsync id cancellationToken
        }

        let! data = SiriusXMClient.getFileAsync chunklist.uri cancellationToken

        let list =
            data.content
            |> Encoding.UTF8.GetString
            |> ChunklistParser.parse

        let content = ChunklistParser.write [
            for segment in list |> List.skip (list.Length - 3) do
                let uri = new Uri(chunklist.uri, segment.path)

                Cache.store $"{chunklist.channelId}-{chunklist.index}-{segment.mediaSequence}" {
                    chunklist = chunklist
                    uri = uri
                    sequenceNumber = segment.mediaSequence
                    encryption =
                        match segment.key with
                        | "METHOD=AES-128,URI=\"key/1\"" -> Key1
                        | "NONE" -> NoEncryption
                        | _ -> raise UnknownEncryptionException
                }

                { segment with key = "NONE"; path = $"chunk-{chunklist.channelId}-{chunklist.index}-{segment.mediaSequence}.ts" }
        ]

        return content
    }

    let getChunkAsync id index sequenceNumber cancellationToken = task {
        let! (chunk: Chunk) = Cache.tryRetrieveWithRetryAsync {
            key = $"{id}-{index}-{sequenceNumber}"
            onRetryAsync = fun () -> getChunklistAsync id index cancellationToken
        }

        let! encryptedData = SiriusXMClient.getFileAsync chunk.uri cancellationToken

        let! data = task {
            match chunk.encryption with
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
                    BinaryPrimitives.WriteUInt128BigEndian(iv.AsSpan(), chunk.sequenceNumber)
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

        return decrypted_data
    }
