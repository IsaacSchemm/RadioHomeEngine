namespace RadioHomeEngine

open System
open System.Threading
open System.Threading.Tasks

module SegmentCacheActivityIndicator =
    let flag = new SemaphoreSlim(1, 1)

    let mutable private callbacks = []

    let notify () =
        flag.Wait()
        try
            for f in callbacks do f ()
            callbacks <- []
        finally
            flag.Release() |> ignore

    let waitForNotifyAsync (cancellationToken: CancellationToken) = task {
        let cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)

        do! flag.WaitAsync(cancellationToken)
        try
            callbacks <- (fun () -> cts.Cancel()) :: callbacks
        finally
            flag.Release() |> ignore

        do! Task.Delay(-1, cts.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing)
    }
