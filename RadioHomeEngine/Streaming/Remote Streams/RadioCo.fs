namespace RadioHomeEngine

open System
open FSharp.Data

module RadioCo =
    type Status = JsonProvider<"""[
      {
        "current_track": {
          "title": "Artist - Song",
          "artwork_url": "https:\/\/www.example.com",
          "artwork_url_large": "https:\/\/www.example.com"
        },
        "logo_url": "https:\/\/www.example.com",
        "streaming_hostname": "stream.radio.co",
        "outputs": [
          {
            "name": "listen",
            "format": "MP3",
            "bitrate": 128
          }
        ]
      },
      {
        "current_track": {
          "title": "Artist - Song"
        },
        "streaming_hostname": "stream.radio.co",
        "outputs": []
      }
    ]""", SampleIsList = true>

    let getByStationIdAsync (name: string) (id: string) cancellationToken = task {
        let statusUrl = $"https://public.radio.co/stations/{Uri.EscapeDataString(id)}/status"

        let! status = Async.StartAsTask(
            Status.AsyncLoad(statusUrl),
            cancellationToken = cancellationToken)

        let output = Array.head status.Outputs

        let programInformationSource = {
            new IProgramInformationSource with
                member _.GetCurrentProgramInformationAsync(cancellationToken) = task {
                    let! status = Async.StartAsTask(
                        Status.AsyncLoad(statusUrl),
                        cancellationToken = cancellationToken)

                    let (artists, title) =
                        match status.CurrentTrack.Title.Split(" - ") with
                        | [| a; b |] -> ([a], b)
                        | _ -> ([], status.CurrentTrack.Title)

                    return {
                        ChannelName = name
                        Program = Song {
                            Title = title
                            Artists = artists
                            Albums = []
                            Images = Option.toList status.CurrentTrack.ArtworkUrlLarge
                        }
                    }
                }
        }

        return {
            new IRemoteStreamSource with
                member _.Name = name
                member _.ProgramInformationSource = programInformationSource
                member _.Url = $"https://{status.StreamingHostname}/{Uri.EscapeDataString(id)}/{Uri.EscapeDataString(output.Name)}"
        }
    }
