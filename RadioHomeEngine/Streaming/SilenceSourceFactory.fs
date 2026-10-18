namespace RadioHomeEngine

open System
open System.Timers
open System.Threading.Tasks

type SilenceSourceFactory() =
    interface ISegmentSourceFactory with
        member _.CreateAsync() = task {
            let mutable buffer = []
            let mutable mediaSequence = UInt128.One

            let! data = Silence.segmentTask.Value

            let interval = TimeSpan.FromSeconds(10.0)

            let timer = new Timer(interval)

            timer.Elapsed.Add(fun args ->
                printfn "[SilenceSource] Adding new segment for next call"

                if List.length buffer < 5 then
                    let segment = {
                        dateTime = new DateTimeOffset(args.SignalTime)
                        duration = interval.TotalSeconds
                        data = data
                        mediaSequence = mediaSequence
                    }
                    buffer <- segment :: buffer
                    mediaSequence <- mediaSequence + UInt128.One)

            timer.Start()

            return {
                new ISegmentSource with
                    member _.ScanAsync(cancellationToken) = task {
                        let list = buffer
                        buffer <- []
                        return List.rev list
                    }

                    member _.IsComplete = false

                    member _.DisposeAsync() = new ValueTask(task {
                        timer.Dispose()
                    })
            }
        }
