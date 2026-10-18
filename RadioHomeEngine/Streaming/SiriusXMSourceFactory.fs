namespace RadioHomeEngine

open System
open System.Diagnostics
open System.IO
open System.Text
open System.Threading
open System.Threading.Tasks
open System.Security.Cryptography
open System.Buffers.Binary

type SiriusXMSourceFactory(channelNumber: int) =
    let sxm = new SiriusXMClient()

    interface ISegmentSourceFactory with
        member _.CreateAsync() = task {
            let mutable sequenceNumber = UInt128.Zero

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

            return {
                new ISegmentSource with
                    member _.ScanAsync(cancellationToken) = task {
                        let! chunklistData = sxm.GetFileAsync(chunklistUri, cancellationToken)

                        let chunklist =
                            chunklistData.content
                            |> Encoding.UTF8.GetString
                            |> ChunklistParser.parse
                            |> ChunklistNormalizer.normalize chunklistUri
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

                                    let ffmpeg =
                                        new ProcessStartInfo(
                                            "ffmpeg",
                                            "-nostats -hide_banner -loglevel warning -i - -f mpegts -c:a copy -",
                                            RedirectStandardInput = true,
                                            RedirectStandardOutput = true)
                                        |> Process.Start

                                    let! recontainerized_data = task {
                                        use buffer = new MemoryStream()

                                        let writeTask = ffmpeg.StandardInput.BaseStream.WriteAsync(data)
                                        let readTask = ffmpeg.StandardOutput.BaseStream.CopyToAsync(buffer)

                                        do! writeTask

                                        ffmpeg.StandardInput.BaseStream.Close()

                                        do! readTask
                                        do! ffmpeg.WaitForExitAsync()

                                        return buffer.ToArray()
                                    }

                                    let segment = {
                                        dateTime = dateTime
                                        duration = duration
                                        data = recontainerized_data
                                        mediaSequence = chunk.mediaSequence
                                    }

                                    segments <- segment :: segments
                                | _ -> ()

                        return segments |> List.sortBy (fun s -> s.dateTime)
                    }

                    member _.IsComplete = false

                    member _.RecommendedInterval = TimeSpan.FromSeconds(5.0)

                    member _.DisposeAsync() = ValueTask.CompletedTask
            }
        }
