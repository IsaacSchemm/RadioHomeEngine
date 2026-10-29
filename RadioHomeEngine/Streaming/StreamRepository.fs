namespace RadioHomeEngine

open System
open System.IO
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open FSharp.Control

/// A module which keeps track of all active radio channels.
module StreamRepository =
    let private permanent = [
        new Composite(
            8000,
            new ShellSegmentSourceFactory(
                "Brown Noise",
                [BrownNoise Infinite]))

        new Composite(
            8001,
            new ShellSegmentSourceFactory(
                "Audio CD",
                [AudioCD AllDrives]))
    ]

    /// All active channels which match a SiriusXM channel number (expected to be within the range 1 through 1999).
    let private sxm = new ResizeArray<Composite>()

    let getAvailableChannelsAsync () = asyncSeq {
        for c in permanent do c.ChannelListing

        let! token = Async.CancellationToken
        let! sxm = SiriusXMClient.GetChannelsAsync(token) |> Async.AwaitTask
        for c in sxm do
            match Int32.TryParse(c.channelNumber) with
            | (false, _) -> ()
            | (true, num) -> {
                ChannelInformation = {
                    ChannelName = c.name
                    ChannelDescriptions = [c.mediumDescription]
                }
                ChannelNumber = num
            }
    }

    /// All currently tracked channels.
    let private getCurrentChannels () = seq {
        yield! permanent
        yield! sxm
    }

    /// All active channels.
    let getActiveChannels () =
        getCurrentChannels ()
        |> Seq.where (fun c -> c.IsActive())
        |> Seq.toList

    /// Gets the tracked composite stream for the given channel number, or creates a new one pointed at the corresponding SiriusXM channel.
    let private getOrCreate channelNumber =
        getCurrentChannels ()
        |> Seq.where (fun s -> s.ChannelListing.ChannelNumber = channelNumber)
        |> Seq.tryHead
        |> Option.defaultWith (fun () ->
            let composite = new Composite(channelNumber, new SiriusXMSourceFactory(channelNumber, 3))
            sxm.Add(composite)
            composite)

    let getPlaylist channelNumber = String.concat "\n" [
        let composite = getOrCreate channelNumber
        composite.MarkActive()

        "#EXTM3U"
        "#EXT-X-ALLOW-CACHE:NO"
        "#EXT-X-VERSION:1"

        "#EXT-X-STREAM-INF:CODECS=\"mp4a.40.2\""

        $"chunklist-{channelNumber}.m3u8"
    ]

    let getChunklist channelNumber = String.concat "\n" [
        let composite = getOrCreate channelNumber
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
        let composite = getOrCreate channelNumber
        composite.MarkActive()
        return! composite.Cache.GetSegmentDataAsync(sequenceNumber)
    }

    /// Creates a task which starts tracking a channel number (if necessary), then waits until at least one segment is available for it.
    let waitForSegmentsAsync channelNumber (cancellationToken: CancellationToken) = task {
        try
            let composite = getOrCreate channelNumber

            composite.MarkActive()

            while composite.Cache.GetSegments() = [] && not cancellationToken.IsCancellationRequested do
                do! Task.Delay(TimeSpan.FromSeconds(0.5), cancellationToken)

            if composite.Cache.GetSegments().Length < 3 then
                do! Task.Delay(TimeSpan.FromSeconds(1.0), cancellationToken)
        with :? OperationCanceledException -> ()
    }

    let getChannelListingAndProgramInformationAsync channelNumber (cancellationToken: CancellationToken) = task {
        let composite = getOrCreate channelNumber

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

    type Service() =
        inherit BackgroundService()

        override _.ExecuteAsync(cancellationToken) = task {
            while not cancellationToken.IsCancellationRequested do
                // Mark composite streams as inactive if they have not been streamed recently.
                // This will tell them to stop listening for new segments from their audio source, and - if the source is the root - to dispose of it, creating a new one later if needed.
                for composite in getCurrentChannels () do
                    composite.CheckForInactivity()

                // Remove and dispose of any inactive SiriusXM streams.
                for composite in List.ofSeq sxm do
                    if not (composite.IsActive()) then
                        printfn "[SegmentCacheRepository] Dropping [%d]" composite.ChannelListing.ChannelNumber
                        sxm.Remove(composite) |> ignore
                        do! (composite :> IAsyncDisposable).DisposeAsync()

                do! Task.Delay(TimeSpan.FromMinutes(1.0), cancellationToken)
        }
