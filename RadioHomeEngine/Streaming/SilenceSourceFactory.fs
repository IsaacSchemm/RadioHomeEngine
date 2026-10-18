namespace RadioHomeEngine

open System
open System.Timers
open System.Threading.Tasks

type SilenceSourceFactory() =
    interface ISegmentSourceFactory with
        member _.CreateAsync() = task {
            let mutable mediaSequence = UInt128.One

            let! data = Silence.segmentTask.Value

            let interval = TimeSpan.FromSeconds(10.0)

            let mutable lastSegmentTime = DateTimeOffset.UtcNow

            return {
                new ILocalSegmentSource with
                    member _.ReadNextAsync(cancellationToken) = task {
                        let nextSegmentTime = lastSegmentTime + interval

                        while DateTimeOffset.UtcNow < nextSegmentTime do
                            do! Task.Delay(TimeSpan.FromSeconds(1.0), cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing)

                        if cancellationToken.IsCancellationRequested then
                            return NoMoreSegments
                        else
                            lastSegmentTime <- nextSegmentTime

                            return NextSegment {
                                dateTime = nextSegmentTime
                                duration = interval.TotalSeconds
                                data = data
                                mediaSequence = mediaSequence
                            }
                    }

                    member _.DisposeAsync() = ValueTask.CompletedTask
            }
        }
