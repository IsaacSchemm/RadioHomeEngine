namespace RadioHomeEngine

open System
open System.IO
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open FSharp.Control

/// A module which keeps track of all active radio channels.
module StreamRepository =
    /// Channel 0. Defaults to silence.
    let private zeroChannel = new Composite(
        0,
        new ShellWrapperSegmentSourceFactory({
            inputs = [Silence Infinite]
            loop = true
        }))

    let private noiseFactory =
        new ShellWrapperSegmentSourceFactory({
            inputs = [BrownNoise Infinite]
            loop = true
        })

    let private test1Factory =
        new ShellWrapperSegmentSourceFactory({
            inputs = [
                let mediaDirs =
                    ["K:/Documents/Music"; "/media/usb/Music"]
                    |> List.where Directory.Exists

                SpeechSynthesis "Lorem ipsum dolor sit amet, consectetur adipiscing elit. Fusce nec diam sed dolor pellentesque efficitur. Vivamus ullamcorper mi leo, at finibus nisl hendrerit eu. Sed porta tempus risus, a vehicula odio tincidunt nec. Donec porta sodales tortor, sed rutrum nunc cursus non. Phasellus iaculis dolor sapien, id aliquam purus gravida et. Etiam euismod ut leo in imperdiet. Etiam commodo varius aliquet. Sed facilisis elementum molestie. Orci varius natoque penatibus et magnis dis parturient montes, nascetur ridiculus mus. Maecenas tempor sollicitudin suscipit. "
                for m in mediaDirs do
                    AudioFile $"{m}/Steps - Gold Greatest Hits/05 Love’s Got a Hold on My Heart.flac"
                SpeechSynthesis "Hello. This a test."
                for m in mediaDirs do
                    AudioFile $"{m}/Steps - Gold Greatest Hits/02 One for Sorrow.flac"
                BrownNoise Infinite
            ]
            loop = false
        })

    /// Channel 8000. Defaults to silence.
    let private noise = new Composite(8000, noiseFactory)

    let private steps = new Composite(8001, test1Factory)

    /// All active channels which match a SiriusXM channel number (expected to be within the range 1 through 1999).
    let private sxm = new ResizeArray<Composite>()

    /// All currently tracked channels.
    let getAll () = seq {
        zeroChannel
        noise
        steps
        yield! sxm
    }

    /// The most recently used action code for channel 0.
    let mutable private lastActionCode = 0

    /// Checks the most recently used action code for channel 0.
    let getActionCode () =
        lastActionCode

    /// Assigns a new action code for channel 0, and updates the channel's audio source accordingly.
    let setActionCodeAsync channelNumber = task {
        lastActionCode <- channelNumber

        do! zeroChannel.ClearAllInterruptionsAsync()

        match lastActionCode with
        | 0 ->
            ()
        | 8001 ->
            do! zeroChannel.InterruptAsync(noiseFactory)
        | 8002 ->
            do! zeroChannel.InterruptAsync(test1Factory)
        | n ->
            do! zeroChannel.InterruptAsync(new SiriusXMSourceFactory(n, 1))
    }

    /// Gets the tracked composite stream for the given channel number, or creates a new one pointed at the corresponding SiriusXM channel.
    let getOrCreate channelNumber =
        getAll()
        |> Seq.where (fun s -> s.ChannelNumber = channelNumber)
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

            while composite.Cache.GetSegments() = [] && not cancellationToken.IsCancellationRequested do
                do! Task.Delay(TimeSpan.FromSeconds(0.5), cancellationToken)

            if composite.Cache.GetSegments().Length < 3 then
                do! Task.Delay(TimeSpan.FromSeconds(1.0), cancellationToken)
        with :? OperationCanceledException -> ()
    }

    type Service() =
        inherit BackgroundService()

        override _.ExecuteAsync(cancellationToken) = task {
            while not cancellationToken.IsCancellationRequested do
                // Mark composite streams as inactive if they have not been streamed recently.
                // This will tell them to stop listening for new segments from their audio source, and - if the source is the root - to dispose of it, creating a new one later if needed.
                for composite in getAll () do
                    composite.CheckForInactivity()

                // Remove and dispose of any inactive SiriusXM streams.
                // This does not apply to SiriusXM channels playing on channel 0, but those will be disposed by the cancellation of the task that reads from them in Composite.fs.
                for composite in List.ofSeq sxm do
                    if not (composite.IsActive()) then
                        printfn "[SegmentCacheRepository] Dropping [%d]" composite.ChannelNumber
                        sxm.Remove(composite) |> ignore
                        do! (composite :> IAsyncDisposable).DisposeAsync()

                do! Task.Delay(TimeSpan.FromMinutes(1.0), cancellationToken)
        }
