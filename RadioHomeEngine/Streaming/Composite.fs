namespace RadioHomeEngine

open System
open System.Threading
open System.Threading.Tasks

type Composite(channelNumber: int, rootFactory: ISegmentSourceFactory) =
    let cache = new SegmentCache()

    let mutable interruptions: ILocalSegmentSource list = []
    let mutable stopWaitingForNextSegment = fun () -> ()

    let overallCancellationTokenSource = new CancellationTokenSource()

    //let runningTask = task {
    //    let overallToken = overallCancellationTokenSource.Token
    //    while not overallToken.IsCancellationRequested do
    //        match interruptions with
    //        | interruption :: _ ->
    //            interruption.TryReadNext
    //}

    member _.Cache = cache
    member _.ChannelNumber = channelNumber

