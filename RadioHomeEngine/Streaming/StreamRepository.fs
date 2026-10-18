namespace RadioHomeEngine

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open FSharp.Control

module StreamRepository =
    let zeroChannel = new Stream({
        channelNumber = 0
        factory = new SilenceSourceFactory()
    })

    let noise = new Stream({
        channelNumber = 8000
        factory = new NoiseSourceFactory()
    })

    module Streams =
        let permanent = [zeroChannel; noise]
        let sxm = new ResizeArray<Stream>()

        let getAll () = seq {
            yield! permanent
            yield! sxm
        }

        let tryGet channelNumber =
            getAll()
            |> Seq.where (fun s -> s.ChannelNumber = channelNumber)
            |> Seq.tryHead

    let mutable channelZero: int option = None

    let getChannelZero () =
        Option.toNullable channelZero

    let setChannelZeroAsync channelNumber = task {
        channelZero <- Option.ofNullable channelNumber

        match channelZero with
        | None ->
            zeroChannel.ClearAllInterruptions()
        | Some 2 ->
            zeroChannel.Interrupt(new SiriusXMSourceFactory(2))
            zeroChannel.Interrupt(new TestSourceFactory())
        | Some n ->
            zeroChannel.Interrupt(new SiriusXMSourceFactory(n))
    }

    let private getStream channelNumber =
        match Streams.tryGet channelNumber with
        | Some stream ->
            stream
        | None ->
            let stream = new Stream({
                channelNumber = channelNumber
                factory = new SiriusXMSourceFactory(channelNumber)
            })
            Streams.sxm.Add(stream)
            stream

    let getPlaylist channelNumber = String.concat "\n" [
        let stream = getStream channelNumber
        ignore stream

        "#EXTM3U"
        "#EXT-X-ALLOW-CACHE:NO"
        "#EXT-X-VERSION:1"

        "#EXT-X-STREAM-INF:CODECS=\"mp4a.40.2\""

        $"chunklist-{channelNumber}.m3u8"
    ]

    let getChunklist channelNumber = String.concat "\n" [
        let stream = getStream channelNumber
        let segments = stream.Cache.GetSegments()
        let finalSegment = segments |> Seq.rev |> Seq.tryHead

        "#EXTM3U"

        match finalSegment with
        | None -> ()
        | Some segment -> $"#EXT-X-TARGETDURATION:{int (round segment.duration)}"

        "#EXT-X-VERSION:1"
        "#EXT-X-ALLOW-CACHE:NO"

        match segments with
        | [] -> ()
        | segment :: _ -> $"#EXT-X-MEDIA-SEQUENCE:{segment.mediaSequence}"

        let mutable encryption = Clear

        for segment in segments do
            $"#EXT-X-PROGRAM-DATE-TIME:{segment.dateTime:o}"
            $"#EXTINF:{segment.duration},"
            $"/Stream/chunk-{channelNumber}-{segment.mediaSequence}.ts"

        if stream.IsComplete then
            "#EXT-X-ENDLIST"
    ]

    let getChunkAsync channelNumber sequenceNumber =
        let stream = getStream channelNumber
        stream.Cache.GetSegmentDataAsync(sequenceNumber)

    let waitForSegmentsAsync channelNumber (cancellationToken: CancellationToken) = task {
        try
            let stream = getStream channelNumber
            while stream.Cache.GetSegments() = [] && not cancellationToken.IsCancellationRequested do
                do! Task.Delay(TimeSpan.FromSeconds(0.5), cancellationToken)
        with :? OperationCanceledException -> ()
    }

    type Service() =
        inherit BackgroundService()

        override _.ExecuteAsync(cancellationToken) = task {
            while not cancellationToken.IsCancellationRequested do
                let waitForNotify = SegmentCacheActivityIndicator.waitForNotifyAsync cancellationToken

                for stream in Streams.getAll () do
                    if stream.Cache.IsInUse() then
                        do! stream.StartAsync(cancellationToken)
                        do! stream.ReadFromBufferAsync()
                    else if not (stream.IsInterrupted()) then
                        do! stream.StopAsync()
                        if Streams.sxm |> Seq.contains stream then
                            printfn "[SegmentCacheRepository] Dropping [%d]" stream.ChannelNumber
                            Streams.sxm.Remove(stream) |> ignore

                do! waitForNotify
        }
