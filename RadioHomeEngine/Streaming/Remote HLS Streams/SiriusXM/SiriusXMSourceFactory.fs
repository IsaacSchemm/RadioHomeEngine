namespace RadioHomeEngine

open System
open System.Buffers.Binary
open System.Diagnostics
open System.IO
open System.Security.Cryptography
open System.Text
open System.Threading
open System.Threading.Channels
open System.Threading.Tasks

/// A media source that decrypts and proxies a SiriusXM stream.
type SiriusXMSourceFactory(channelNumber: int, initialCount: int) =
    let sxm = new SiriusXMClient()

    let mutable channelName = None

    interface IProgramInformationSource with
        member _.GetCurrentProgramInformationAsync(cancellationToken) = task {
            let! channels = sxm.GetChannelsAsync(cancellationToken)

            let channel =
                channels
                |> Seq.where (fun c -> c.channelNumber = $"{channelNumber}")
                |> Seq.tryHead

            let! playlist = sxm.GetPlaylistAsync(channelNumber, CancellationToken.None)

            let cut =
                playlist.cuts
                |> Seq.sortByDescending (fun cut -> cut.startTime)
                |> Seq.tryHead

            match (channel, cut) with
            | Some ch, Some s ->
                return {
                    channelName = ch.name
                    programName = String.concat " - " [
                        if s.artists <> [] then String.concat ", " s.artists
                        s.title
                    ]
                }
            | _ ->
                return {
                    channelName = $"[{channelNumber}] Unknown"
                    programName = "Unknown"
                }
        }

    interface ISegmentSourceFactory with
        member this.ChannelName =
            channelName
            |> Option.defaultValue $"SiriusXM {channelNumber}"

        member this.ProgramInformationSource = this

        member this.CreateAsync() = task {
            let! channels = sxm.GetChannelsAsync(CancellationToken.None)

            channelName <-
                channels
                |> Seq.where (fun c -> c.channelNumber = $"{channelNumber}")
                |> Seq.map (fun c -> c.name)
                |> Seq.tryHead

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

            let getChunklistAsync count cancellationToken = task {
                let! chunklistData = sxm.GetFileAsync(chunklistUri, cancellationToken)

                let chunklist =
                    chunklistData.content
                    |> Encoding.UTF8.GetString
                    |> ChunklistParser.parse
                    |> ChunklistNormalizer.normalize chunklistUri
                    |> Seq.rev
                    |> Seq.truncate count
                    |> Seq.rev

                return chunklist
            }

            let decryptAsync (chunk: UpstreamSegment) (data: byte array) = task {
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
                        do! cryptoStream.WriteAsync(data)
                    }

                    return outputStream.ToArray()
                | Clear ->
                    return data
                | x ->
                    return failwithf "Unrecognized stream encryption: %A" x
            }

            let recontainerizeAsync (data: byte array) = task {
                use ffmpeg =
                    new ProcessStartInfo(
                        "ffmpeg",
                        "-nostats -hide_banner -loglevel warning -i - -f mpegts -c:a copy -",
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true)
                    |> Process.Start

                use buffer = new MemoryStream()

                let writeTask = ffmpeg.StandardInput.BaseStream.WriteAsync(data)
                let readTask = ffmpeg.StandardOutput.BaseStream.CopyToAsync(buffer)

                do! writeTask

                ffmpeg.StandardInput.BaseStream.Close()

                do! readTask
                do! ffmpeg.WaitForExitAsync()

                return buffer.ToArray()
            }

            let internalBuffer = Channel.CreateBounded<DownstreamSegment>(3)

            let adapterCancellationTokenSource = new CancellationTokenSource()

            let activeTask = task {
                let cancellationToken = adapterCancellationTokenSource.Token

                try
                    let mutable count = initialCount
                    while not cancellationToken.IsCancellationRequested do
                        let minimumDelay = Task.Delay(5000, cancellationToken)

                        let! chunklist = getChunklistAsync count cancellationToken
                        count <- Int32.MaxValue

                        for chunk in chunklist do
                            if sequenceNumber < chunk.mediaSequence then
                                sequenceNumber <- chunk.mediaSequence

                                match chunk.dateTime, chunk.duration with
                                | Some dateTime, Some duration ->
                                    let chunkUri = new Uri(chunklistUri, chunk.path)

                                    let! file = sxm.GetFileAsync(chunkUri, cancellationToken)

                                    let! data = decryptAsync chunk file.content

                                    let! recontainerized_data = recontainerizeAsync data

                                    let segment = {
                                        dateTime = dateTime
                                        duration = duration
                                        data = recontainerized_data
                                        mediaSequence = chunk.mediaSequence
                                        important = false
                                    }

                                    do! internalBuffer.Writer.WriteAsync(segment, cancellationToken)
                                | _ -> ()

                        do! minimumDelay
                with :? OperationCanceledException when cancellationToken.IsCancellationRequested -> ()

                internalBuffer.Writer.Complete()

                printfn "[SiriusXMSourceFactory] Complete"
            }

            return {
                new ISegmentSource with
                    member _.ProgramInformationSource = this

                    member _.ReadNextAsync(cancellationToken) =  task {
                        let! more = internalBuffer.Reader.WaitToReadAsync(cancellationToken)

                        if more then
                            let! segment = internalBuffer.Reader.ReadAsync(cancellationToken)
                            printfn "[SiriusXMSourceFactory] Returning segment %A" segment.mediaSequence
                            return NextSegment segment
                        else
                            return NoMoreSegments
                    }

                    member _.DisposeAsync() = ValueTask (task {
                        printfn "[SiriusXMSourceFactory] Disposing"
                        do! adapterCancellationTokenSource.CancelAsync()
                        do! activeTask
                    })
            }
    }
