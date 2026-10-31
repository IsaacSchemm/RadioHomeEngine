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

    let all = asyncSeq {
        let tzid = "America/Chicago"
        let tz = TimeZoneInfo.FindSystemTimeZoneById(tzid)
        let local = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.UtcNow,
            tz)
        let! resp = ScheduleResponse.AsyncLoad($"""https://player.pbs.org/api/livestream-schedule/a3a25e36-3ba3-40b4-990f-7460511f0127/?date={local.Date.ToString("yyyy-MM-dd")}&timezone={Uri.EscapeDataString(tzid)}""")

        let channels =
            resp.Collections
            |> Seq.collect (fun c -> c.Content)
            |> Seq.collect (fun c -> c.Channels)

        for channel in channels do
            match channel.NonDrmUrl with
            | None -> ()
            | Some url ->
                let programInformationSource = {
                    new IProgramInformationSource with
                        member _.GetCurrentProgramInformationAsync(cancellationToken) = task {
                            let local = TimeZoneInfo.ConvertTimeFromUtc(
                                DateTime.UtcNow,
                                tz)
                            let! resp = ScheduleResponse.AsyncLoad($"""https://player.pbs.org/api/livestream-schedule/a3a25e36-3ba3-40b4-990f-7460511f0127/?date={local.Date.ToString("yyyy-MM-dd")}&timezone={Uri.EscapeDataString(tzid)}""")
                            let channels =
                                resp.Collections
                                |> Seq.collect (fun c -> c.Content)
                                |> Seq.collect (fun c -> c.Channels)
                            let channelNow =
                                channels
                                |> Seq.where (fun c -> c.Profile = channel.Profile)
                                |> Seq.tryHead
                            let name =
                                channelNow
                                |> Option.map (fun c -> c.FullName)
                                |> Option.defaultValue channel.Profile
                            let listing =
                                channelNow
                                |> Option.map (fun c -> c.Listings)
                                |> Option.defaultValue Array.empty
                                |> Seq.where (fun p ->
                                    let now = DateTimeOffset.UtcNow
                                    let endTime = p.StartTime + TimeSpan.FromSeconds(float p.Duration)
                                    DateTimeOffset.UtcNow >= p.StartTime && DateTimeOffset.UtcNow < endTime)
                                |> Seq.tryHead
                            return {
                                ChannelName = name
                                Program =
                                    match listing with
                                    | None -> Program name
                                    | Some l -> Song {
                                        Title = l.ShowTitle
                                        Artists = Option.toList l.EpisodeTitle
                                        Albums = []
                                        Images = Option.toList l.ListingImage
                                    }
                            }
                        }
                }

                yield new UnauthenticatedHLSSourceFactory(
                    channel.FullName,
                    url,
                    Some programInformationSource)
    }
