namespace RadioHomeEngine

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open FSharp.Control

module StreamRepository =
    let zeroChannel = new Stream({
        channelNumber = 0
        mode = Resumable (fun () -> new SilenceSource())
    })

    let noise = new Stream({
        channelNumber = 8000
        mode = Resumable (fun () -> new NoiseSource())
    })

    let streams = ResizeArray [zeroChannel; noise]

    let mutable channelZero: int option = None

    let getChannelZero () =
        Option.toNullable channelZero

    let setChannelZeroAsync channelNumber = task {
        channelZero <- Option.ofNullable channelNumber

        let mutable at_root = false
        while not at_root do
            let! popped = noise.PopAsync()
            at_root <- not popped

        match channelZero with
        | None -> ()
        | Some num ->
            do! noise.PushAsync(new SiriusXMSource(num))
    }

    let getStream channelNumber = lock streams (fun () ->
        match streams |> Seq.where (fun s -> s.ChannelNumber = channelNumber) |> Seq.tryHead with
        | Some stream ->
            stream
        | None ->
            let stream = new Stream({
                channelNumber = channelNumber
                mode = Ephemeral (new SiriusXMSource(channelNumber))
            })
            streams.Add(stream)
            stream)

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

        match finalSegment with
        | None -> ()
        | Some segment -> $"#EXT-X-MEDIA-SEQUENCE:{segment.mediaSequence}"

        let mutable encryption = Clear

        for segment in segments do
            $"#EXT-X-PROGRAM-DATE-TIME:{segment.dateTime:o}"
            $"#EXTINF:{segment.duration},"
            $"/Stream/chunk-{channelNumber}-{segment.mediaSequence}.ts"
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
                let active =
                    streams
                    |> Seq.where (fun s -> s.Cache.IsInUse())
                    |> Seq.toList

                let inactive =
                    streams
                    |> Seq.except active
                    |> Seq.toList

                for stream in inactive do
                    match stream.Mode with
                    | Ephemeral _ ->
                        printfn "[SegmentCacheRepository] Dropping [%d]" stream.ChannelNumber
                        ignore (lock streams (fun () -> streams.Remove(stream)))
                    | Resumable _ -> ()

                    do! stream.StopAsync()

                for stream in active do
                    do! stream.StartAsync(cancellationToken)
                    stream.Read()

                do! Task.WhenAll [
                    Task.Delay(TimeSpan.FromSeconds(1.0), cancellationToken)
                    SegmentCacheActivityIndicator.waitForNotifyAsync cancellationToken
                ]
        }
