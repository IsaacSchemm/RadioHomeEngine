namespace RadioHomeEngine

open System.IO

module Shuffle =
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

    let byFolderNamePattern pattern =
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
        |> Seq.randomShuffle
        |> Seq.map AudioFile
        |> Seq.toList
