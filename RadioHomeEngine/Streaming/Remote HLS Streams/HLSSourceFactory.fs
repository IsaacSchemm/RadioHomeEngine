namespace RadioHomeEngine

open System
open System.Buffers.Binary
open System.Diagnostics
open System.IO
open System.Net
open System.Net.Http
open System.Security.Cryptography
open System.Text
open System.Threading
open System.Threading.Channels
open System.Threading.Tasks

//https://wpnedt2.lls.pbs.org/wpnedt2-hls.m3u8 38.2
//https://wpnedt3.lls.pbs.org/wpnedt3-hls.m3u8 38.3

module private HLSSourceFactory =
    let handler = new HttpClientHandler()
    handler.AutomaticDecompression <- DecompressionMethods.GZip ||| DecompressionMethods.Deflate

    let client = new HttpClient(handler)

/// A media source that proxies an unencrypted HLS stream.
type HLSSourceFactory(name: string, url: string) =
    let initialCount = 3

    let getTextFileAsync (uri: Uri) (cancellationToken: CancellationToken) = task {
        use! finalResponse = HLSSourceFactory.client.GetAsync(
            uri,
            cancellationToken)

        let! text = finalResponse.EnsureSuccessStatusCode().Content.ReadAsStringAsync(cancellationToken)

        return {|
            content = text
            contentType = finalResponse.Content.Headers.ContentType.MediaType
        |}
    }

    let getFileAsync (uri: Uri) (cancellationToken: CancellationToken) = task {
        use! finalResponse = HLSSourceFactory.client.GetAsync(
            uri,
            cancellationToken)

        use! stream = finalResponse.EnsureSuccessStatusCode().Content.ReadAsStreamAsync(cancellationToken)

        use ms = new MemoryStream()
        do! stream.CopyToAsync(ms)
        let data = ms.ToArray()

        return {|
            content = data
            contentType = finalResponse.Content.Headers.ContentType.MediaType
        |}
    }

    interface IProgramInformationSource with
        member _.GetCurrentProgramInformationAsync(cancellationToken) = task {
            return {
                ChannelName = name
                Program = Program name
            }
        }

    interface ISegmentSourceFactory with
        member this.ChannelInformation = {
            ChannelName = name
            ChannelDescriptions = []
        }

        member this.ProgramInformationSource = this

        member this.CreateAsync() = task {
            let mutable sequenceNumber = UInt128.Zero

            let playlistUri = new Uri(url)

            let! playlistData = getTextFileAsync playlistUri CancellationToken.None

            let chunklistPath =
                playlistData.content
                |> Utility.split '\n'
                |> Seq.where (fun line -> not (line.StartsWith('#')))
                |> Seq.head

            let chunklistUri = new Uri(playlistUri, chunklistPath)

            let getChunklistAsync count cancellationToken = task {
                let! chunklistData = getTextFileAsync chunklistUri cancellationToken

                let chunklist =
                    chunklistData.content
                    |> ChunklistParser.parse
                    |> ChunklistNormalizer.normalize chunklistUri
                    |> Seq.rev
                    |> Seq.truncate count
                    |> Seq.rev

                return chunklist
            }

            let decryptAsync (chunk: UpstreamSegment) (data: byte array) = task {
                match chunk.encryption with
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

                                    let! file = getFileAsync chunkUri cancellationToken

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

                printfn "[HLSSourceFactory] Complete"
            }

            return {
                new ISegmentSource with
                    member _.ProgramInformationSource = this

                    member _.ReadNextAsync(cancellationToken) =  task {
                        let! more = internalBuffer.Reader.WaitToReadAsync(cancellationToken)

                        if more then
                            let! segment = internalBuffer.Reader.ReadAsync(cancellationToken)
                            printfn "[HLSSourceFactory] Returning segment %A" segment.mediaSequence
                            return NextSegment segment
                        else
                            return NoMoreSegments
                    }

                    member _.DisposeAsync() = ValueTask (task {
                        printfn "[HLSSourceFactory] Disposing"
                        do! adapterCancellationTokenSource.CancelAsync()
                        do! activeTask
                    })
            }
    }
