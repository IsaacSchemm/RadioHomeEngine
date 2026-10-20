namespace RadioHomeEngine

open System
open System.Timers
open System.Threading.Tasks

type SilenceSourceFactory() =
    let programInformationSource = {
        new IProgramInformationSource with
            member _.GetCurrentProgramInformationAsync(cancellationToken) = Task.FromResult({
                channelName = "Silence"
                title = "Silence"
                artists = []
                albums = []
            })
    }

    interface ISegmentSourceFactory with
        member _.ProgramInformationSource = programInformationSource

        member _.CreateAsync() = task {
            let mutable mediaSequence = UInt128.One

            let! data = Silence.segmentTask.Value

            let interval = TimeSpan.FromSeconds(10.0)

            let mutable lastSegmentTime = DateTimeOffset.UtcNow

            return {
                new ISegmentSource with
                    member _.ProgramInformationSource = programInformationSource

                    member _.ReadNextAsync(cancellationToken) = task {
                        let nextSegmentTime = lastSegmentTime + interval

                        while DateTimeOffset.UtcNow < nextSegmentTime do
                            let ts = max (nextSegmentTime - DateTimeOffset.UtcNow) (TimeSpan.FromSeconds(0.1))
                            do! Task.Delay(ts, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing)

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
