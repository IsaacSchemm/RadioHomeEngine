namespace RadioHomeEngine

open System
open System.IO
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open FSharp.Control

/// A module which keeps track of all active radio channels.
module StreamRepository =
    let silence = new Composite(
        2000,
        new ShellSegmentSourceFactory(
            "Silence",
            10,
            [Silence Infinite]))

    let private permanent = [
        silence

        new Composite(
            2001,
            new ShellSegmentSourceFactory(
                "Brown Noise",
                10,
                [BrownNoise Infinite]))

        let sec = TimeSpan.FromSeconds(1.0)

        new Composite(
            2002,
            new ShellSegmentSourceFactory(
                "Seconds Counter",
                10,
                [
                    for _ in [0 .. 4] do
                        BrownNoise (Finite sec)
                        Silence (Finite sec)
                ]))

        new Composite(
            2003,
            new ShellSegmentSourceFactory(
                "Audio CD",
                10,
                [AudioCD AllDrives]))
    ]

    let private ephemeral = new ResizeArray<Composite>()

    /// All currently tracked channels.
    let getCurrentChannels () = seq {
        yield! permanent
        yield! ephemeral
    }

    /// All active channels.
    let getActiveChannels () =
        getCurrentChannels ()
        |> Seq.where (fun c -> c.IsActive())
        |> Seq.toList

    /// Gets the tracked composite stream for the given channel number, or creates a new one pointed at the corresponding SiriusXM channel.
    let private get channelNumber =
        getCurrentChannels ()
        |> Seq.where (fun s -> s.ChannelListing.ChannelNumber = channelNumber)
        |> Seq.tryHead
        |> Option.defaultValue silence

    let getPlaylist channelNumber = String.concat "\n" [
        let composite = get channelNumber
        composite.MarkActive()

        "#EXTM3U"
        "#EXT-X-ALLOW-CACHE:NO"
        "#EXT-X-VERSION:1"

        "#EXT-X-STREAM-INF:CODECS=\"mp4a.40.2\""

        $"chunklist-{channelNumber}.m3u8"
    ]

    let getChunklist channelNumber = String.concat "\n" [
        let composite = get channelNumber
        composite.MarkActive()

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

    let getChunkAsync channelNumber sequenceNumber = task {
        let composite = get channelNumber
        composite.MarkActive()
        return! composite.Cache.GetSegmentDataAsync(sequenceNumber)
    }

    /// Creates a task which starts tracking a channel number (if necessary), then waits until at least one segment is available for it.
    let waitForSegmentsAsync channelNumber (cancellationToken: CancellationToken) = task {
        try
            let composite = get channelNumber

            composite.MarkActive()

            while composite.Cache.GetSegments() = [] && not cancellationToken.IsCancellationRequested do
                do! Task.Delay(TimeSpan.FromSeconds(0.5), cancellationToken)

            if composite.Cache.GetSegments().Length < 3 then
                do! Task.Delay(TimeSpan.FromSeconds(1.0), cancellationToken)
        with :? OperationCanceledException -> ()
    }

    let getChannelListingAndProgramInformationAsync channelNumber (cancellationToken: CancellationToken) = task {
        let composite = get channelNumber

        composite.MarkActive()

        while composite.Cache.GetSegments() = [] && not cancellationToken.IsCancellationRequested do
            do! Task.Delay(TimeSpan.FromSeconds(0.5), cancellationToken)

        if composite.Cache.GetSegments().Length < 3 then
            do! Task.Delay(TimeSpan.FromSeconds(1.0), cancellationToken)

        let! programInformation = composite.GetCurrentStreamInformationAsync(cancellationToken)
        return {|
            channelListing = composite.ChannelListing
            programInformation = programInformation
        |}
    }

    type ChannelListingRefreshService() =
        inherit BackgroundService()

        override _.ExecuteAsync(cancellationToken) = task {
            while not cancellationToken.IsCancellationRequested do
                if ephemeral |> Seq.where (fun c -> c.IsActive()) |> Seq.isEmpty then
                    let old = List.ofSeq ephemeral
                    ephemeral.Clear()
                    for c in old do
                        printfn "[SegmentCacheRepository] Dropping [%d]" c.ChannelListing.ChannelNumber
                        do! (c :> IAsyncDisposable).DisposeAsync()

                    let channelExists n =
                        getCurrentChannels ()
                        |> Seq.exists (fun c -> c.ChannelListing.ChannelNumber = n)

                    let! sxm = SiriusXMClient.GetChannelsAsync(cancellationToken) |> Async.AwaitTask
                    for c in sxm do
                        match Int32.TryParse(c.channelNumber) with
                        | (true, num) when not (channelExists num) ->
                            ephemeral.Add(new Composite(
                                num,
                                new SiriusXMSourceFactory(c.name, c.mediumDescription, num)))
                        | _ -> ()

                    let mutable nextChannelNumber =
                        getCurrentChannels ()
                        |> Seq.map (fun c -> c.ChannelListing.ChannelNumber + 1)
                        |> Seq.max

                    let! wpr = WPRStreamSource.all |> AsyncSeq.toListAsync
                    for c in wpr do
                        ephemeral.Add(new Composite(
                            nextChannelNumber,
                            new ShellSegmentSourceFactory(c.Name, 4, [RemoteStream c])))
                        nextChannelNumber <- nextChannelNumber + 1

                    let! pbs = PBSWisconsin.all |> AsyncSeq.toListAsync
                    for f in pbs do
                        ephemeral.Add(new Composite(nextChannelNumber, f))
                        nextChannelNumber <- nextChannelNumber + 1

                do! Task.Delay(TimeSpan.FromHours(12.0), cancellationToken)
        }

    type CleanupService() =
        inherit BackgroundService()

        override _.ExecuteAsync(cancellationToken) = task {
            while not cancellationToken.IsCancellationRequested do
                // Mark composite streams as inactive if they have not been streamed recently.
                // This will tell them to stop listening for new segments from their audio source, and - if the source is the root - to dispose of it, creating a new one later if needed.
                for composite in getCurrentChannels () do
                    composite.CheckForInactivity()

                do! Task.Delay(TimeSpan.FromMinutes(1.0), cancellationToken)
        }
