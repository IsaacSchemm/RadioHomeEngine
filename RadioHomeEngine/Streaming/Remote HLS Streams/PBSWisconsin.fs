namespace RadioHomeEngine

open System
open System.Threading
open System.Threading.Tasks
open FSharp.Control
open FSharp.Data

module PBSWisconsin =
    type ScheduleResponse = JsonProvider<"""{
      "collections": [
        {
          "item_type": "schedule",
          "content": [
            {
              "schedule_type": "live",
              "channels": [
                {
                  "profile": "xyz",
                  "non_drm_url": null,
                  "feed_image": {
                    "color_logo": "https://www.example.com",
                    "white_logo": "https://www.example.com"
                  },
                  "digital_channel": "2.1",
                  "full_name": "Name",
                  "timezone": "America/Chicago",
                  "listings": [
                    {
                      "start_time": "2026-10-29T05:00:00Z",
                      "duration": 1800,
                      "show_title": "Title",
                      "episode_title": "Title",
                      "item_type": "listing",
                      "show_description": "Description",
                      "episode_description": "Description",
                      "listing_image": "https://www.example.com"
                    }
                  ],
                  "item_type": "channel"
                },
                {
                  "profile": "xyz",
                  "non_drm_url": "https://www.example.com",
                  "feed_image": {},
                  "digital_channel": "2.1",
                  "full_name": "Name",
                  "timezone": "America/Chicago",
                  "listings": [
                    {
                      "start_time": "2026-10-29T05:00:00Z",
                      "duration": 3600,
                      "show_title": "Title",
                      "item_type": "listing",
                      "show_description": "Description",
                      "episode_description": null
                    }
                  ],
                  "item_type": "channel"
                }
              ],
              "item_type": "schedule"
            }
          ]
        }
      ]
    }""">
    
    let private tzid = "America/Chicago"

    let private getLocalTime () = TimeZoneInfo.ConvertTimeFromUtc(
        DateTime.UtcNow,
        TimeZoneInfo.FindSystemTimeZoneById(tzid))

    let private asyncGetSchedule =
        let dateString = (getLocalTime ()).ToString("yyyy-MM-dd")
        ScheduleResponse.AsyncLoad($"""https://player.pbs.org/api/livestream-schedule/a3a25e36-3ba3-40b4-990f-7460511f0127/?date={dateString}&timezone={Uri.EscapeDataString(tzid)}""")

    let private asyncGetChannels = async {
        let dateString = (getLocalTime ()).ToString("yyyy-MM-dd")
        let! resp = ScheduleResponse.AsyncLoad($"""https://player.pbs.org/api/livestream-schedule/a3a25e36-3ba3-40b4-990f-7460511f0127/?date={dateString}&timezone={Uri.EscapeDataString(tzid)}""")

        return
            resp.Collections
            |> Seq.collect (fun c -> c.Content)
            |> Seq.collect (fun c -> c.Channels)
    }

    let private getProgramInformationSource profile = {
        new IProgramInformationSource with
            member _.GetCurrentProgramInformationAsync(cancellationToken) = task {
                let! channels = asyncGetChannels
                return {
                    ChannelName =
                        channels
                        |> Seq.where (fun c -> c.Profile = profile)
                        |> Seq.map (fun c -> c.FullName)
                        |> Seq.tryHead
                        |> Option.defaultValue $"{profile}"
                    Program =
                        channels
                        |> Seq.where (fun c -> c.Profile = profile)
                        |> Seq.collect (fun c -> c.Listings)
                        |> Seq.where (fun p ->
                            let now = DateTimeOffset.UtcNow
                            let endTime = p.StartTime + TimeSpan.FromSeconds(float p.Duration)
                            DateTimeOffset.UtcNow >= p.StartTime && DateTimeOffset.UtcNow < endTime)
                        |> Seq.tryHead
                        |> Option.map (fun l -> Song {
                            Title = l.ShowTitle
                            Artists = Option.toList l.EpisodeTitle
                            Albums = []
                            Images = Option.toList l.ListingImage
                        })
                        |> Option.defaultValue (Program "Unknown")
                }
            }
    }

    let all = asyncSeq {
        let! channels = asyncGetChannels

        for channel in channels do
            match channel.NonDrmUrl with
            | None -> ()
            | Some url ->
                yield new UnauthenticatedHLSSourceFactory(
                    channel.FullName,
                    url,
                    Some (getProgramInformationSource channel.Profile))
    }
