namespace RadioHomeEngine

open System
open System.IO
open System.Diagnostics
open FSharp.Control
open FSharp.Data
open System.Text.RegularExpressions

module MusicFiles =
    let private extensionPatterns = [
        "*.aac"
        "*.aif"
        "*.aiff"
        "*.flac"
        "*.mp3"
        "*.m4a"
        "*.oga"
        "*.ogg"
        "*.wav"
        "*.wma"
    ]

    let byArtistFolder (artist: string) =
        Config.mediaDirectories
        |> Seq.collect (fun mediaDir -> seq {
            yield! Directory.EnumerateDirectories(
                mediaDir,
                $"*{artist}*",
                new EnumerationOptions(RecurseSubdirectories = true))

            yield! Directory.EnumerateDirectories(
                mediaDir,
                $"""*{artist.Replace(" ", "_")}*""",
                new EnumerationOptions(RecurseSubdirectories = true))
        })
        |> Seq.collect (fun subdirectory -> seq {
            for extensionPattern in extensionPatterns do
                yield! Directory.EnumerateFiles(
                    subdirectory,
                    extensionPattern,
                    new EnumerationOptions(RecurseSubdirectories = true))
        })
        |> Seq.distinct
        |> Seq.toList
