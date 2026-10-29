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

/// A media source that proxies an HLS stream.
[<AbstractClass>]
type HLSSourceFactory() =
    let initialCount = 3

    abstract member ChannelInformation: ChannelInformation with get
    abstract member ProgramInformationSource: IProgramInformationSource with get

    abstract member DecryptSegmentAsync: UpstreamSegment * byte array -> byte array Task
    abstract member GetFileAsync: Uri * CancellationToken -> byte array Task
    abstract member GetPlaylistUriAsync: unit -> Uri Task
    abstract member GetTextFileAsync: Uri * CancellationToken -> string Task

    interface ISegmentSourceFactory with
        member this.ChannelInformation = this.ChannelInformation

        member this.ProgramInformationSource = this.ProgramInformationSource

        member this.CreateAsync() = task {
            let mutable sequenceNumber = UInt128.Zero

            let! playlistUri = this.GetPlaylistUriAsync()

            let! playlistData = this.GetTextFileAsync(playlistUri, CancellationToken.None)

            let chunklistPath =
                playlistData
                |> Utility.split '\n'
                |> Seq.where (fun line -> not (line.StartsWith('#')))
                |> Seq.head

            let chunklistUri = new Uri(playlistUri, chunklistPath)

            let getChunklistAsync count cancellationToken = task {
                let! chunklistData = this.GetTextFileAsync(chunklistUri, cancellationToken)

                let chunklist =
                    chunklistData
                    |> ChunklistParser.parse
                    |> ChunklistNormalizer.normalize chunklistUri
                    |> Seq.rev
                    |> Seq.truncate count
                    |> Seq.rev

                return chunklist
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

                                    let! file = this.GetFileAsync(chunkUri, cancellationToken)

                                    let! data = this.DecryptSegmentAsync(chunk, file)

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
                    member _.ProgramInformationSource =
                        this.ProgramInformationSource

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
