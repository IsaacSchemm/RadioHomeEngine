namespace RadioHomeEngine

open System
open System.IO
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open FSharp.Control

/// A module which keeps track of all active radio channels.
module StreamRepository =
    let defaultChannel = new Composite(
        0,
        new ShellSegmentSourceFactory(
            "Channel Not Found",
            10,
            [Silence Infinite]))

    let private ephemeral = new ResizeArray<Composite>()

    /// All currently tracked channels.
    let getCurrentChannels () = ephemeral :> Composite seq

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
        |> Option.defaultValue defaultChannel

    let getPlaylist channelNumber = String.concat "\n" [
        let composite = get channelNumber
        composite.MarkActive()

        "#EXTM3U"
        "#EXT-X-ALLOW-CACHE:NO"
        "#EXT-X-VERSION:1"

        $"#EXT-X-STREAM-INF:BANDWIDTH={composite.Cache.NominalBitrate},CODECS=\"mp4a.40.2\""

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
            $"chunk-{channelNumber}-{segment.mediaSequence}.ts"
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

                    let add desiredChannelNumber factory =
                        let inUse n =
                            getCurrentChannels ()
                            |> Seq.map (fun c -> c.ChannelListing.ChannelNumber)
                            |> Seq.contains n

                        let channelNumber = Seq.head (seq {
                            for i in desiredChannelNumber .. 9999 do
                                if not (inUse i) then i
                            for i in Int32.MinValue .. -1 do
                                if not (inUse i) then i
                        })

                        ephemeral.Add(
                            new Composite(
                                channelNumber,
                                factory))

                    let! sxm = SiriusXMClient.GetChannelsAsync(cancellationToken) |> Async.AwaitTask
                    for c in sxm do
                        match Int32.TryParse(c.channelNumber) with
                        | (true, num) ->
                             add num (new SiriusXMSourceFactory(c.name, c.mediumDescription, num))
                        | _ -> ()

                    add 1000 (new ShellSegmentSourceFactory(
                        "Silence",
                        10,
                        [Silence Infinite]))

                    add 1000 (new ShellSegmentSourceFactory(
                        "Brown Noise",
                        10,
                        [BrownNoise Infinite]))

                    add 1000 (new ShellSegmentSourceFactory(
                        "Alternating Noise",
                        4,
                        [
                            let sec = TimeSpan.FromSeconds(1.0)
                            BrownNoise (Finite sec)
                            Silence (Finite sec)
                            BrownNoise (Finite sec)
                            Silence (Finite sec)
                        ]))

                    //add 1000 (new ShellSegmentSourceFactory(
                    //    "Audio CD",
                    //    10,
                    //    [AudioCD AllDrives]))

                    for d in DiscDrive.getAll () do
                        add 1000 (new ShellSegmentSourceFactory(
                            DiscDrive.getPath d,
                            10,
                            [AudioCD (SingleDrive d)]))

                    let! pbs = PBSWisconsin.all |> AsyncSeq.toListAsync
                    for c in pbs do
                        let desiredNumber =
                            match c.channelNumber.Split('.') with
                            | [| Int32 38; Int32 minor |] -> 3800 + minor
                            | _ -> 1000

                        add desiredNumber (c.factory)

                    let! wpr = WPRStreamSource.all |> AsyncSeq.toListAsync
                    for c in wpr do
                        let desiredNumber =
                            match c.Name with
                            | "WPR Music" -> 9300
                            | "WPR News" -> 9700
                            | _ -> 1000

                        add desiredNumber (new ShellSegmentSourceFactory(c.Name, 4, [RemoteStream c]))

                do! Task.Delay(TimeSpan.FromHours(6.0), cancellationToken)
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
