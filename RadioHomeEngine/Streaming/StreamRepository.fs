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
        0,
        Silence.factory,
        ClearCacheOnInactive = false)

    let playCD = new Composite(
        2000,
        new ShellSegmentSourceFactory(
            "Audio CD",
            [CD (AllDrives, AllTracks)]),
        ClearCacheOnInactive = false)

    let shuffleCD = new Composite(
        2020,
        new ShellSegmentSourceFactory(
            "Audio CD (Shuffle)",
            [CD (AllDrives, ShuffleCD)]),
        ClearCacheOnInactive = false)

    let brownNoise = new Composite(
        3010,
        new ShellSegmentSourceFactory(
            "Brown Noise",
            [BrownNoise Infinite]),
        ClearCacheOnInactive = false)

    let forecast = new Composite(
        3020,
        new ShellSegmentSourceFactory(
            "Forecast",
            [
                let halfSecond = TimeSpan.FromSeconds(0.5) |> Finite

                Forecast

                BrownNoise halfSecond
                Silence halfSecond
            ]),
        ClearCacheOnInactive = false)

    let private ephemeral = new ResizeArray<Composite>()

    /// All currently tracked channels.
    let getCurrentChannels () = seq {
        silence
        playCD
        shuffleCD
        brownNoise
        forecast
        yield! ephemeral
    }

    /// All active channels.
    let getActiveChannels () =
        getCurrentChannels ()
        |> Seq.where (fun c -> c.IsActive())
        |> Seq.toList

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

    let private add (list: ResizeArray<Composite>) desiredChannelNumber factory =
        let mutable channelNumber = desiredChannelNumber
        while getCurrentChannels () |> Seq.exists (fun c -> c.ChannelListing.ChannelNumber = channelNumber) do
            channelNumber <- channelNumber + 1

        list.Add(
            new Composite(
                channelNumber,
                factory))

    let private addShell desiredChannelNumber displayName inputs =
        add ephemeral desiredChannelNumber (new ShellSegmentSourceFactory(displayName, inputs))

    type ChannelListingRefreshService() =
        inherit BackgroundService()

        let populateAllAsync cancellationToken = task {
            let! sxm = SiriusXMClient.GetChannelsAsync(cancellationToken) |> Async.AwaitTask
            for c in sxm do
                match Int32.TryParse(c.channelNumber) with
                | (true, num) ->
                        add ephemeral num (new SiriusXMSourceFactory(c.name, c.mediumDescription, num))
                | _ -> ()

            try
                for artist in List.sort ["Lisa Stansfield"; "Steps"; "They Might Be Giants"; "Jamie Paige"; "Dionne Warwick"] do
                    let shuffle = Shuffle (MusicFiles.byArtistFolder artist)
                    addShell 1000 artist [shuffle]
            with ex -> Console.Error.WriteLine(ex)

            for (i, drive) in Seq.indexed (DiscDrive.getAll ()) do
                addShell (2001 + i) (DiscDrive.getPath drive) [CD (SingleDrive drive, AllTracks)]
                addShell (2021 + i) (DiscDrive.getPath drive) [CD (SingleDrive drive, ShuffleCD)]

            for card in ALSA.getCards () do
                for device in card.devices do
                    addShell (2100 + card.number * 10 + device.number) device.id [ALSA (card.number, device.number)]

            addShell 9630 "WHYS 96.3 FM" [RemoteStream WHYS.source]

            try
                let! converge = RadioCo.getByStationIdAsync "Converge Radio" "s5fa0df0d2" cancellationToken
                addShell 10190 "Converge Radio 101.9 FM" [RemoteStream converge]
            with ex -> Console.Error.WriteLine(ex)

            try
                let! wpr = WPR.all |> AsyncSeq.toListAsync
                for source in wpr do
                    let desiredNumber =
                        match source.Name with
                        | "WPR Music" -> 8970
                        | "WPR News" -> 8830
                        | _ -> 20000

                    addShell desiredNumber source.Name [RemoteStream source]
            with ex -> Console.Error.WriteLine(ex)

            add ephemeral 30180 (new HLSSourceFactory(
                "WQOW 18 News",
                "https://streamingengineservice-prod01-v1.fuelmedia.io/play/6a60c7ae-75e2-465f-bd30-1b1506880293.m3u8",
                None))

            try
                let! pbs = PBSWisconsin.all |> AsyncSeq.toListAsync
                for source in pbs do
                    let desiredNumber =
                        match source.DigitalChannel.Split('.') with
                        | [| Int32 major; Int32 minor |] -> 30000 + 10 * major + minor
                        | _ -> 30000

                    addShell desiredNumber source.FullName [RemoteStream source]
            with ex -> Console.Error.WriteLine(ex)
        }

        override _.ExecuteAsync(cancellationToken) = task {
            while not cancellationToken.IsCancellationRequested do
                if ephemeral |> Seq.where (fun c -> c.IsActive()) |> Seq.isEmpty then
                    let old = List.ofSeq ephemeral

                    ephemeral.Clear()

                    for c in old do
                        printfn "[StreamRepository] Dropping [%d]" c.ChannelListing.ChannelNumber
                        do! (c :> IAsyncDisposable).DisposeAsync()

                    do! populateAllAsync cancellationToken

                do! Task.Delay(TimeSpan.FromHours(6.0), cancellationToken)
        }

    type CleanupService() =
        inherit BackgroundService()

        override _.ExecuteAsync(cancellationToken) = task {
            while not cancellationToken.IsCancellationRequested do
                // Mark composite streams as inactive if they have not been streamed recently.
                // This will tell them to stop listening for new segments from their audio source, and - if the source is the root - to dispose of it, creating a new one later if needed.
                for composite in getCurrentChannels () do
                    do! composite.CheckForInactivityAsync()

                do! Task.Delay(TimeSpan.FromMinutes(1.0), cancellationToken)
        }
