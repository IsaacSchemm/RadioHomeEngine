namespace RadioHomeEngine

open System
open System.Threading
open System.Threading.Tasks
open FSharp.Control
open FSharp.Data

module WPRStreamSource =
    type PlayerResponse = JsonProvider<"""[
      {
        "id": 99999,
        "name": "Name",
        "audioUrl": "https:\/\/www.example.com",
        "scheduleUrl": "https:\/\/www.example.com",
        "schedule": [
          {
            "showTitle": "Title",
            "showUrl": "https:\/\/www.example.com",
            "startTime": 0,
            "endTime": 21540,
            "daysOfWeek": [ 1, 2, 3, 4, 5 ],
            "friendlyStartTime": "12AM"
          }
        ]
      }
    ]""">
    
    let private tzid = "America/Chicago"

    let private getLocalTime () = TimeZoneInfo.ConvertTimeFromUtc(
        DateTime.UtcNow,
        TimeZoneInfo.FindSystemTimeZoneById(tzid))

    let private asyncGetStreams =
        let tz = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago")
        PlayerResponse.AsyncLoad("https://www.wpr.org/wp-json/wpr/v1/player")

    let private getProgramInformationSource id = {
        new IProgramInformationSource with
            member _.GetCurrentProgramInformationAsync(cancellationToken) = task {
                let local = getLocalTime ()
                let time = int (local - local.Date).TotalSeconds
                let! streams = asyncGetStreams
                let streamNow =
                    streams
                    |> Seq.where (fun s -> s.Id = id)
                    |> Seq.tryHead
                return {
                    ChannelName =
                        streams
                        |> Seq.where (fun s -> s.Id = id)
                        |> Seq.map (fun s -> s.Name)
                        |> Seq.tryHead
                        |> Option.defaultValue $"{id}"
                    Program =
                        streams
                        |> Seq.where (fun s -> s.Id = id)
                        |> Seq.collect (fun s -> s.Schedule)
                        |> Seq.where (fun p -> time >= p.StartTime && time < p.EndTime)
                        |> Seq.map (fun p -> Program p.ShowTitle)
                        |> Seq.tryHead
                        |> Option.defaultValue (Program "Unknown")
                }
            }
    }

    let all = asyncSeq {
        let! resp = asyncGetStreams

        for stream in resp do yield {
            new IRemoteStreamSource with
                member _.Name = stream.Name
                member _.ProgramInformationSource = getProgramInformationSource stream.Id
                member _.Url = stream.AudioUrl
        }
    }
