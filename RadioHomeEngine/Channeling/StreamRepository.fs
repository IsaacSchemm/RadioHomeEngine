namespace RadioHomeEngine

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open FSharp.Control

module StreamRepository =
    let noise = new Stream({
        channelNumber = 0
        factory = fun () -> new NoiseSource()
        mode = Pausable
    })

    let streams = ResizeArray [noise]

    [<Obsolete>]
    let pushToChannelZeroAsync channelNumber =
        noise.PushAsync(fun () -> new SiriusXMSource(channelNumber))

    [<Obsolete>]
    let restoreChannelZeroAsync () =
        noise.PopAsync()

    let getStream channelNumber = lock streams (fun () ->
        match streams |> Seq.where (fun s -> s.ChannelNumber = channelNumber) |> Seq.tryHead with
        | Some c ->
            printfn "[SegmentCacheRepository] Finding [%d]" channelNumber
            c
        | None ->
            printfn "[SegmentCacheRepository] Creating [%d]" channelNumber
            let s = new Stream({
                channelNumber = channelNumber
                factory = fun () -> new SiriusXMSource(channelNumber)
                mode = Ephemeral
            })
            streams.Add(s)
            s)

    let forgetStreamAsync stream = task {
        let _ = lock streams (fun () -> streams.Remove(stream))
        printfn "[SegmentCacheRepository] Forgetting [%d]" stream.ChannelNumber
        do! stream.StopAsync()
    }

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

    let getChunk channelNumber sequenceNumber =
        let stream = getStream channelNumber
        stream.Cache.GetSegmentData(sequenceNumber)

    let waitForSegmentsAsync channelNumber (cancellationToken: CancellationToken) = task {
        let stream = getStream channelNumber
        while stream.Cache.GetSegments() = [] && not cancellationToken.IsCancellationRequested do
            printfn "[SegmentCacheRepository] Waiting for segments [%d]" channelNumber
            do! Task.Delay(TimeSpan.FromSeconds(0.5), cancellationToken)
    }

    type Service() =
        inherit BackgroundService()

        override _.ExecuteAsync(cancellationToken) = task {
            while not cancellationToken.IsCancellationRequested do
                let active =
                    streams
                    |> Seq.where (fun s -> s.Mode = Permanent || s.Cache.IsInUse())
                    |> Seq.toList

                let inactive =
                    streams
                    |> Seq.except active
                    |> Seq.toList

                for channel in inactive do
                    if channel.Mode = Pausable then
                        do! channel.StopAsync()
                    else
                        do! forgetStreamAsync channel

                for channel in active do
                    printfn "[SegmentCacheRepository] Checking [%d]" channel.ChannelNumber
                    do! channel.StartAsync(cancellationToken)
                    channel.Read()

                do! Task.WhenAll [
                    Task.Delay(TimeSpan.FromSeconds(1.0), cancellationToken)
                    SegmentCacheActivityIndicator.waitForNotifyAsync cancellationToken
                ]
        }
