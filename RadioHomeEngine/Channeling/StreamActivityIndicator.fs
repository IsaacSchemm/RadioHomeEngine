namespace RadioHomeEngine

open System
open System.Threading
open System.Threading.Tasks

module SegmentCacheActivityIndicator =
    let mutable private callbacks = []

    let notify () =
        for f in callbacks do f ()
        callbacks <- []

    let waitForNotifyAsync (cancellationToken: CancellationToken) = task {
        use cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
        callbacks <- (fun () -> cts.Cancel()) :: callbacks
        do! Task.Delay(-1, cts.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing)
    }
