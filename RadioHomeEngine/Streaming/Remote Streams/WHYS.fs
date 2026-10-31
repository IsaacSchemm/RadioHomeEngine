namespace RadioHomeEngine

open System
open System.IO
open System.Text.RegularExpressions
open FSharp.Data

module WHYS =
    let private name = "WHYS 96.3 FM"

    let private expression = new Regex("^[^ ]+ +[^ ]+ +\"([^\"]*)\" +- +\"([^\"]*)\" +Duration: *[^ ]+ +Album: *\"([^\"]*)\"")

    let private notNothing = not << String.IsNullOrWhiteSpace

    let programInformationSource = {
        new IProgramInformationSource with
            member this.GetCurrentProgramInformationAsync(cancellationToken) = task {
                let! resp = Async.StartAsTask(
                    FSharp.Data.Http.AsyncRequestString("https://www.whysradio.org/playingnow.txt"),
                    cancellationToken = cancellationToken)

                use sr = new StringReader(resp)
                let! line = sr.ReadLineAsync(cancellationToken)

                let m = expression.Match(line)

                return {
                    ChannelName = name
                    Program =
                        if m.Success
                        then Song {
                            Title = m.Groups[1].Value
                            Artists = List.where notNothing [m.Groups[2].Value]
                            Albums = List.where notNothing [m.Groups[3].Value]
                            Images = []
                        }
                        else Program name
                }
            }
        }

    let source = {
        new IRemoteStreamSource with
            member this.Name = name
            member this.ProgramInformationSource = programInformationSource
            member this.Url = "https://whysradio.org/live"
    }
