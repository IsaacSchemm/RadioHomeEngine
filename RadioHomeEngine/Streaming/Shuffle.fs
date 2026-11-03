namespace RadioHomeEngine

open System
open System.IO
open System.Diagnostics
open FSharp.Control
open FSharp.Data

module Shuffle =
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

    let artists = [
        "Lisa Stansfield"
        "Steps"
    ]

    let extensionPatterns = [
        "*.wav"
        "*.flac"
        "*.ogg"
        "*.mp3"
        "*.mp4"
        "*.m4a"
        "*.aac"
        "*.wma"
    ]

    let asyncGetInfo path = async {
        try
            use proc = Process.Start(
                new ProcessStartInfo(
                    "ffprobe",
                    "-v quiet -print_format json -show_format -",
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true))

            let! token = Async.CancellationToken

            use writeTask = task {
                try
                    use fs = new FileStream(path, FileMode.Open, FileAccess.Read)
                    use stream = proc.StandardInput.BaseStream
                    do! fs.CopyToAsync(stream, token)
                with :? IOException when proc.HasExited -> ()
            }

            use sr = proc.StandardOutput
            let! json = sr.ReadToEndAsync(token) |> Async.AwaitTask

            do! Async.AwaitTask writeTask

            let obj = ProbeOutput.Parse(json)

            let date =
                obj.Format
                |> Option.bind (fun f -> f.Tags)
                |> Option.bind (fun t ->
                    t.Date
                    |> Option.orElse t.Date2)

            let title =
                obj.Format
                |> Option.bind (fun f -> f.Tags)
                |> Option.bind (fun t ->
                    t.Title
                    |> Option.orElse t.Title2)
                |> Option.defaultValue path

            return {|
                date = date
                path = path
                title = title
            |}
        with ex ->
            Console.Error.WriteLine(ex)
            return {|
                date = None
                path = path
                title = path
            |}
    }

    let asyncByFolderNamePattern pattern = asyncSeq {
        let paths =
            Config.mediaDirectories
            |> Seq.collect (fun mediaDir -> Directory.EnumerateDirectories(
                mediaDir,
                pattern,
                new EnumerationOptions(RecurseSubdirectories = true)))
            |> Seq.collect (fun subdirectory -> seq {
                for extensionPattern in extensionPatterns do
                    yield! Directory.EnumerateFiles(
                        subdirectory,
                        extensionPattern,
                        new EnumerationOptions(RecurseSubdirectories = true))
            })
            |> Seq.distinct

        let! files =
            paths
            |> AsyncSeq.ofSeq
            |> AsyncSeq.mapAsync asyncGetInfo
            |> AsyncSeq.toListAsync

        let uniqueFiles =
            files
            |> Seq.sortBy (fun i -> i.date)
            |> Seq.distinctBy (fun i -> i.title)
            |> Seq.toList

        for i in uniqueFiles do
            printfn "%A (%A)" i.title i.date
            AudioFile i.path
    }

    let asyncByArtist (artist: string) = asyncSeq {
        yield! asyncByFolderNamePattern $"*{artist}*"
        yield! asyncByFolderNamePattern $"""*{artist.Replace(" ", "_")}*"""
    }
