namespace RadioHomeEngine

open System
open System.Threading
open System.Threading.Tasks

/// A multiplexed composite audio stream associated with a specific radio channel number.
/// Maintains a segment cache, a "root" audio source, and a set of "interruptions" that consist of other, likely finite, audio sources.
type Composite(channelNumber: int, rootFactory: ISegmentSourceFactory) =
    let cache = new SegmentCache()

    let mutable interruptions: ISegmentSource list = []
    let mutable isActive = true
    let mutable lastActive = DateTimeOffset.UtcNow
    let mutable stopWaitingForNextSegment = fun () -> ()

    let overallCancellationTokenSource = new CancellationTokenSource()

    let endInterruptionAsync () = task {
        match interruptions with
        | [] -> ()
        | head :: tail ->
            interruptions <- tail
            do! head.DisposeAsync()
    }

    let readAllFromSourceAsync (source: ISegmentSource) (cancellationToken: CancellationToken) = task {
        try
            let mutable finished = false
            while not finished && not cancellationToken.IsCancellationRequested do
                printfn "[Composite] Waiting for next segment [%d]" channelNumber
                let! next = source.ReadNextAsync(cancellationToken)
                match next with
                | NextSegment segment ->
                    printfn "[Composite] Found next segment %A [%d]" segment.mediaSequence channelNumber
                    cache.Add(segment)
                | NoMoreSegments ->
                    printfn "[Composite] Found end [%d]" channelNumber
                    finished <- true
        with :? OperationCanceledException when cancellationToken.IsCancellationRequested ->
            printfn "[Composite] No longer waiting for next segment (operation cancelled) [%d]" channelNumber
    }

    let runningTask = task {
        let overallToken = overallCancellationTokenSource.Token
        while not overallToken.IsCancellationRequested do
            printfn "[Composite] Starting outer loop [%d]" channelNumber

            use cts = CancellationTokenSource.CreateLinkedTokenSource(overallToken)
            stopWaitingForNextSegment <- fun () -> cts.Cancel()

            try
                printfn "[Composite] Starting inner loop [%d]" channelNumber
                while not cts.IsCancellationRequested do
                    if not isActive then
                        do! Task.Delay(-1, cts.Token)
                    else
                        match List.tryHead interruptions with
                        | Some interruption ->
                            do! readAllFromSourceAsync interruption cts.Token
                            do! endInterruptionAsync ()
                        | None ->
                            use! rootSource = rootFactory.CreateAsync()
                            do! readAllFromSourceAsync rootSource cts.Token
            with :? OperationCanceledException when cts.IsCancellationRequested ->
                printfn "[Composite] Inner loop cancelled [%d]" channelNumber

        printfn "[Composite] Outer loop cancelled [%d]" channelNumber
    }

    member _.Cache = cache
    member _.ChannelNumber = channelNumber

    member _.GetCurrentStreamInformationAsync(cancellationToken) = task {
        let programInformationSource =
            interruptions
            |> Seq.map (fun i -> i.ProgramInformationSource)
            |> Seq.tryHead
            |> Option.defaultValue rootFactory.ProgramInformationSource

        return! programInformationSource.GetCurrentProgramInformationAsync(cancellationToken)
    }

    member this.Interrupt(source: ISegmentSource) =
        interruptions <- source :: interruptions
        stopWaitingForNextSegment ()

    member this.InterruptAsync(factory: ISegmentSourceFactory) = task {
       let! source = factory.CreateAsync()
       this.Interrupt(source)
    }

    member this.ClearAllInterruptionsAsync() = task {
        while interruptions <> [] do
            do! endInterruptionAsync ()
        stopWaitingForNextSegment ()
    }

    member this.IsActive() =
        isActive

    member this.MarkActive() =
        lastActive <- DateTimeOffset.UtcNow
        if not isActive then
            printfn "[Composite] Marking active [%d]" channelNumber
            isActive <- true
            stopWaitingForNextSegment ()

    member this.CheckForInactivity() =
        if isActive && DateTimeOffset.UtcNow - lastActive > TimeSpan.FromMinutes(1.0) then
            printfn "[Composite] Marking inactive [%d]" channelNumber
            isActive <- false
            stopWaitingForNextSegment ()

    interface IAsyncDisposable with
        member this.DisposeAsync() = ValueTask (task {
            printfn "[Composite] Disposing [%d]" channelNumber
            do! overallCancellationTokenSource.CancelAsync()
            printfn "[Composite] Waiting for task [%d]" channelNumber
            do! runningTask
            printfn "[Composite] Disposed [%d]" channelNumber
        })
