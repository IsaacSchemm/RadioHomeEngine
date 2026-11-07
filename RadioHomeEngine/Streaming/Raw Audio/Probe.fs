namespace RadioHomeEngine

open System
open System.Diagnostics
open System.IO
open System.Threading
open FSharp.Data
open System.Text.RegularExpressions

module Probe =
    type private ProbeOutput = JsonProvider<"""[
        {
            "format": {
                "start_time": "0.000000",
                "duration": "261.500000",
                "tags": {
                    "TITLE": "xxx",
                    "ARTIST": "xxx",
                    "ALBUM": "xxx",
                    "DATE": "2000"
                }
            }
        },
        {
            "format": {
                "start_time": "0.025000",
                "duration": "116.500000",
                "tags": {
                    "title": "xxx",
                    "artist": "xxx",
                    "album": "xxx",
                    "date": "2000"
                }
            }
        },
        {
            "format": {}
        },
        {}
    ]""", SampleIsList = true>

    let getAudioFileInfoAsync path (cancellationToken: CancellationToken) = task {
        try
            use proc = Process.Start(
                new ProcessStartInfo(
                    "ffprobe",
                    "-v quiet -print_format json -show_format -",
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true))

            use writeTask = task {
                try
                    use fs = new FileStream(path, FileMode.Open, FileAccess.Read)
                    use stream = proc.StandardInput.BaseStream
                    do! fs.CopyToAsync(stream, cancellationToken)
                with :? IOException when proc.HasExited -> ()
            }

            use sr = proc.StandardOutput
            let! json = sr.ReadToEndAsync(cancellationToken) |> Async.AwaitTask

            do! Async.AwaitTask writeTask

            let obj = ProbeOutput.Parse(json)

            let title =
                obj.Format
                |> Option.bind (fun f -> f.Tags)
                |> Option.map (fun t -> [t.Title; t.Title2])
                |> Option.defaultValue []
                |> Seq.choose id
                |> Seq.tryHead
                |> Option.defaultValue path

            let artists =
                obj.Format
                |> Option.bind (fun f -> f.Tags)
                |> Option.map (fun t -> [t.Artist; t.Artist2])
                |> Option.defaultValue []
                |> List.choose id

            let albums =
                obj.Format
                |> Option.bind (fun f -> f.Tags)
                |> Option.map (fun t -> [t.Album; t.Album2])
                |> Option.defaultValue []
                |> List.choose id

            let dates =
                obj.Format
                |> Option.bind (fun f -> f.Tags)
                |> Option.map (fun t -> [t.Date; t.Date2])
                |> Option.defaultValue []
                |> List.choose id

            return Song {
                Title = title
                Artists = artists
                Albums =
                    match (albums, dates) with
                    | ([a], [d]) -> [$"{a} ({d})"]
                    | _ -> albums
                Images = []
            }

        with ex ->
            Console.Error.WriteLine(ex)
            return Program (Path.GetFileName(path))
    }
