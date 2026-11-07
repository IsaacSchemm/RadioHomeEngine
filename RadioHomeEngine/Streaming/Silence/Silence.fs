namespace RadioHomeEngine

open System
open System.Diagnostics
open System.IO
open System.Threading.Tasks

module Silence =
    let segmentDuration = 10.0

    let generator = lazy task {
        let proc = new ProcessStartInfo(
            "ffmpeg",
            $"-nostats -hide_banner -loglevel warning -f lavfi -i anullsrc=cl=stereo:sample_rate=44100 -t {segmentDuration} -f mpegts -",
            RedirectStandardOutput = true) |> Process.Start

        use readTask = task {
            use stdout = proc.StandardOutput.BaseStream
            use ms = new MemoryStream()
            do! stdout.CopyToAsync(ms)
            return ms.ToArray()
        }

        do! proc.WaitForExitAsync()

        return! readTask
    }

    let programInformationSource = {
        new IProgramInformationSource with
            member _.GetCurrentProgramInformationAsync(_) = Task.FromResult({
                ChannelName = "Silence"
                Program = Program "Silence"
            })
    }

    let factory = {
        new ISegmentSourceFactory with
            member _.ChannelInformation = {
                ChannelName = "Silence"
                ChannelDescriptions = []
            }

            member _.ProgramInformationSource = programInformationSource

            member _.CreateAsync() = task {
                let mutable nextSegmentTime = DateTimeOffset.UtcNow.AddSeconds(-30)
                let mutable nextSequenceNumber = UInt128.One

                return {
                    new ISegmentSource with
                        member _.ProgramInformationSource = programInformationSource

                        member _.ReadNextAsync(cancellationToken) = task {
                            let oldestAcceptableTime = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(30.0)
                            while nextSegmentTime < oldestAcceptableTime do
                                nextSegmentTime <- nextSegmentTime + TimeSpan.FromSeconds(segmentDuration)
                                nextSequenceNumber <- nextSequenceNumber + UInt128.One

                            let delay = nextSegmentTime - DateTimeOffset.UtcNow
                            if delay > TimeSpan.Zero then
                                do! Task.Delay(delay, cancellationToken)

                            let! data = generator.Force()

                            let segment = {
                                dateTime = nextSegmentTime
                                duration = segmentDuration
                                data = data
                                mediaSequence = nextSequenceNumber
                                important = false
                            }

                            nextSegmentTime <- nextSegmentTime + TimeSpan.FromSeconds(segmentDuration)
                            nextSequenceNumber <- nextSequenceNumber + UInt128.One

                            return NextSegment segment
                        }

                        member _.DisposeAsync() = ValueTask.CompletedTask
                }
            }
    }
