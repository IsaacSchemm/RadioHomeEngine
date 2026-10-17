namespace RadioHomeEngine

open System
open System.Diagnostics
open System.IO
open System.Text
open System.Threading
open System.Threading.Tasks
open System.Security.Cryptography
open System.Buffers.Binary

type SiriusXMSource(channelNumber: int) =
    let sxm = new SiriusXMClient()

    let mutable sequenceNumber = UInt128.Zero

    let startTask = lazy task {
        let! playlist = sxm.GetPlaylistAsync(channelNumber, CancellationToken.None)

        let playlistUri = new Uri(playlist.url)

        let! playlistData = sxm.GetFileAsync(playlistUri, CancellationToken.None)

        let chunklistPath =
            playlistData.content
            |> Encoding.UTF8.GetString
            |> Utility.split '\n'
            |> Seq.where (fun line -> not (line.StartsWith('#')))
            |> Seq.head

        let chunklistUri = new Uri(playlistUri, chunklistPath)

        return chunklistUri
    }

    interface ISegmentSource with
        member _.ScanAsync(cancellationToken) = task {
            let! chunklistUri = startTask.Value

            let! chunklistData = sxm.GetFileAsync(chunklistUri, cancellationToken)

            let chunklist =
                chunklistData.content
                |> Encoding.UTF8.GetString
                |> ChunklistParser.parse
                |> Seq.rev
                |> Seq.truncate 3
                |> Seq.rev

            let mutable segments = []

            for chunk in chunklist do
                if sequenceNumber < chunk.mediaSequence then
                    sequenceNumber <- chunk.mediaSequence

                    match chunk.dateTime, chunk.duration with
                    | Some dateTime, Some duration ->
                        let chunkUri = new Uri(chunklistUri, chunk.path)

                        let! encryptedData = sxm.GetFileAsync(chunkUri, cancellationToken)

                        let! data = task {
                            match chunk.encryption with
                            | Aes128 "key/1" ->
                                use algorithm = Aes.Create()
                                algorithm.Padding <- PaddingMode.PKCS7
                                algorithm.Mode <- CipherMode.CBC
                                algorithm.KeySize <- 128
                                algorithm.BlockSize <- 128

                                algorithm.Key <- sxm.GetKey()

                                algorithm.IV <-
                                    let iv = Array.zeroCreate 16
                                    BinaryPrimitives.WriteUInt128BigEndian(iv.AsSpan(), chunk.mediaSequence)
                                    iv

                                use outputStream = new MemoryStream()

                                do! task {
                                    use cryptoStream = new CryptoStream(outputStream, algorithm.CreateDecryptor(), CryptoStreamMode.Write)
                                    do! cryptoStream.WriteAsync(encryptedData.content)
                                }

                                return outputStream.ToArray()
                            | Clear ->
                                return encryptedData.content
                            | x ->
                                return failwithf "Unrecogznied stream encryption: %A" x
                        }

                        let segment = {
                            dateTime = dateTime
                            duration = duration
                            data = data
                            mediaSequence = chunk.mediaSequence
                        }

                        segments <- segment :: segments
                    | _ -> ()

            return segments |> List.sortBy (fun s -> s.dateTime)
        }

    interface IAsyncDisposable with
        member _.DisposeAsync() = ValueTask.CompletedTask
