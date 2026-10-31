namespace RadioHomeEngine

open System
open System.Threading
open System.Threading.Tasks
open FSharp.Control
open FSharp.Data

module WPRStreamSource =
    type PlayerResponse = JsonProvider<"""[
      {
        "id": 87969,
        "name": "WPR Music",
        "audioUrl": "https:\/\/wpr-ice.streamguys1.com\/wpr-music-mp3-96",
        "scheduleUrl": "https:\/\/www.wpr.org\/network\/wpr-music",
        "schedule": [
          {
            "showTitle": "Overnight Classics",
            "showUrl": "https:\/\/www.wpr.org\/shows\/overnight-classics",
            "startTime": 0,
            "endTime": 21540,
            "daysOfWeek": [ 1, 2, 3, 4, 5 ],
            "friendlyStartTime": "12AM"
          }
        ]
      }
    ]""">

    let all = asyncSeq {
        let tz = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago")
        let! resp = PlayerResponse.AsyncLoad("https://www.wpr.org/wp-json/wpr/v1/player?_locale=user")

        for stream in resp do yield {
            new IRemoteStreamSource with
                member _.Name = stream.Name

                member _.ProgramInformationSource = {
                    new IProgramInformationSource with
                        member _.GetCurrentProgramInformationAsync(cancellationToken) = task {
                            let id = stream.Id
                            let local = TimeZoneInfo.ConvertTimeFromUtc(
                                DateTime.UtcNow,
                                tz)
                            let time = int (local - local.Date).TotalSeconds
                            let! resp = Async.StartAsTask(
                                PlayerResponse.AsyncLoad("https://www.wpr.org/wp-json/wpr/v1/player?_locale=user"),
                                cancellationToken = cancellationToken)
                            let streamNow =
                                resp
                                |> Seq.where (fun s -> s.Id = id)
                                |> Seq.tryHead
                            let name =
                                streamNow
                                |> Option.map (fun s -> s.Name)
                                |> Option.defaultValue $"{id}"
                            let program =
                                streamNow
                                |> Option.map (fun s -> s.Schedule)
                                |> Option.defaultValue Array.empty
                                |> Seq.where (fun p -> time >= p.StartTime && time < p.EndTime)
                                |> Seq.map (fun p -> p.ShowTitle)
                                |> Seq.tryHead
                                |> Option.defaultValue name
                            return {
                                ChannelName = name
                                Program = Program program
                            }
                        }
                }

                member _.Url = stream.AudioUrl
        }
    }
