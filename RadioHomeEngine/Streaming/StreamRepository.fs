namespace RadioHomeEngine

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open FSharp.Control

module StreamRepository =
    let zeroChannel = new Composite(0, new SilenceSourceFactory())

    let noise = new Composite(8000, new NoiseSourceFactory())

    module Composites =
        let permanent = [zeroChannel; noise]
        let sxm = new ResizeArray<Composite>()

        let getAll () = [
            yield! permanent
            yield! sxm
        ]

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
            do! zeroChannel.ClearAllInterruptionsAsync()
        | Some 2 ->
            do! zeroChannel.InterruptAsync(new SiriusXMSourceFactory(2))
            do! zeroChannel.InterruptAsync(new TestSourceFactory())
        | Some n ->
            do! zeroChannel.InterruptAsync(new SiriusXMSourceFactory(n))
    }

    let private getComposite channelNumber =
        match Composites.tryGet channelNumber with
        | Some stream ->
            stream
        | None ->
            let composite = new Composite(channelNumber, new SiriusXMSourceFactory(channelNumber))
            Composites.sxm.Add(composite)
            composite

    let getPlaylist channelNumber = String.concat "\n" [
        let composite = getComposite channelNumber
        ignore composite

        "#EXTM3U"
        "#EXT-X-ALLOW-CACHE:NO"
        "#EXT-X-VERSION:1"

        "#EXT-X-STREAM-INF:CODECS=\"mp4a.40.2\""

        $"chunklist-{channelNumber}.m3u8"
    ]

    let getChunklist channelNumber = String.concat "\n" [
        let composite = getComposite channelNumber
        let segments = composite.Cache.GetSegments()
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

        //if stream.IsComplete then
        //    "#EXT-X-ENDLIST"
    ]

    let getChunkAsync channelNumber sequenceNumber =
        let composite = getComposite channelNumber
        composite.Cache.GetSegmentDataAsync(sequenceNumber)

    let waitForSegmentsAsync channelNumber (cancellationToken: CancellationToken) = task {
        try
            let composite = getComposite channelNumber
            while composite.Cache.GetSegments() = [] && not cancellationToken.IsCancellationRequested do
                do! Task.Delay(TimeSpan.FromSeconds(0.5), cancellationToken)
        with :? OperationCanceledException -> ()
    }

    type Service() =
        inherit BackgroundService()

        override _.ExecuteAsync(cancellationToken) = task {
            while not cancellationToken.IsCancellationRequested do
                let waitForNotify = SegmentCacheActivityIndicator.waitForNotifyAsync cancellationToken

                for composite in Composites.getAll () do
                    if composite.Cache.IsInUse() then
                        printfn "[SegmentCacheRepository] Marking active [%d]" composite.ChannelNumber
                        composite.MarkActive()
                    else
                        printfn "[SegmentCacheRepository] Marking inactive [%d]" composite.ChannelNumber
                        composite.MarkInactive()
                        if Composites.sxm |> Seq.contains composite then
                            printfn "[SegmentCacheRepository] Dropping [%d]" composite.ChannelNumber
                            Composites.sxm.Remove(composite) |> ignore
                            do! (composite :> IAsyncDisposable).DisposeAsync()

                printfn "[SegmentCacheRepository] Waiting"
                let! _ = Task.WhenAny(Task.Delay(30000), waitForNotify)
                ()
        }
