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
            [Silence Infinite]))

    let silence = new Composite(
        3000,
        new ShellSegmentSourceFactory(
            "Silence",
            [Silence Infinite]),
        ClearCacheOnInactive = false)

    let brownNoise = new Composite(
        3001,
        new ShellSegmentSourceFactory(
            "Brown Noise",
            [BrownNoise Infinite]),
        ClearCacheOnInactive = false)

    let private disc = new ResizeArray<Composite>()
    let private ephemeral = new ResizeArray<Composite>()

    /// All currently tracked channels.
    let getCurrentChannels () = seq {
        silence
        brownNoise
        yield! disc
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

    let private add (list: ResizeArray<Composite>) desiredChannelNumber factory =
        let mutable channelNumber = desiredChannelNumber
        while getCurrentChannels () |> Seq.exists (fun c -> c.ChannelListing.ChannelNumber = channelNumber) do
            channelNumber <- channelNumber + 1

        list.Add(
            new Composite(
                channelNumber,
                factory))

    let private addShell list desiredChannelNumber displayName inputs =
        add list desiredChannelNumber (new ShellSegmentSourceFactory(displayName, inputs))

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
                    let! files = Shuffle.byArtistAsync artist cancellationToken
                    addShell ephemeral 1000 artist [for f in files do AudioFile f]
            with ex -> Console.Error.WriteLine(ex)

            addShell ephemeral 3000 "Forecast" [Forecast]

            if File.Exists("/usr/bin/wwvsim") || File.Exists("/usr/local/bin/wwvsim") then
                addShell ephemeral 3000 "Time Signal" [WWVSimulator]

            if Directory.Exists("C:/Windows/Media") then
                addShell ephemeral 3000 "Windows Alarms" [
                    for f in Directory.EnumerateFiles("C:/Windows/Media", "*.wav") do
                        if Path.GetFileName(f).StartsWith("Alarm") then
                            AudioFile f
                ]

            addShell ephemeral 9630 "WHYS 96.3 FM" [RemoteStream WHYS.source]

            try
                let! converge = RadioCo.getByStationIdAsync "Converge Radio" "s5fa0df0d2" cancellationToken
                addShell ephemeral 10190 "Converge Radio 101.9 FM" [RemoteStream converge]
            with ex -> Console.Error.WriteLine(ex)

            try
                let! wpr = WPR.all |> AsyncSeq.toListAsync
                for source in wpr do
                    let desiredNumber =
                        match source.Name with
                        | "WPR Music" -> 20930
                        | "WPR News" -> 20970
                        | _ -> 20000

                    addShell ephemeral desiredNumber source.Name [RemoteStream source]
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

                    addShell ephemeral desiredNumber source.FullName [RemoteStream source]
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

    module AudioCDChannelListing =
        let repopulateAllAsync cancellationToken = task {
            let old = List.ofSeq disc

            for c in old do
                printfn "[AudioCDChannelListing] Dropping [%d]" c.ChannelListing.ChannelNumber
                disc.Remove(c) |> ignore
                do! (c :> IAsyncDisposable).DisposeAsync()

            try
                let getTracks dd = [
                    match DiscDriveStatus.tryGetAudioDiscInfo dd with
                    | None -> ()
                    | Some audioDiscInfo ->
                        for t in audioDiscInfo.tracks do
                            let song = {
                                Title = t.title
                                Artists = audioDiscInfo.artists
                                Albums = audioDiscInfo.titles
                                Images = []
                            }
                            AudioCDTrack (dd, t.position, song)
                ]

                let allTracks = [
                    for dd in DiscDrive.getAll () do
                        yield! getTracks dd
                ]

                if allTracks <> [] then
                    addShell disc 2000 "Play CD (All)" [AudioCD AllDrives]
                    addShell disc 2020 $"Shuffle CD (All)" (List.randomShuffle allTracks)

                for dd in DiscDrive.getAll () do
                    let title =
                        DiscDriveStatus.tryGetAudioDiscInfo dd
                        |> Option.bind (fun i -> List.tryHead i.titles)
                        |> Option.defaultValue (DiscDrive.getPath dd)

                    let tracks = getTracks dd

                    if tracks <> [] then
                        addShell disc 2000 $"Play CD ({title})" [AudioCD (SingleDrive dd)]
                        addShell disc 2020 $"Shuffle CD ({title})" (List.randomShuffle tracks)
            with ex -> Console.Error.WriteLine(ex)
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
