namespace RadioHomeEngine

open System
open System.IO

module ALSA =
    let private tryReadFile path =
        if File.Exists(path)
        then Some (File.ReadAllText(path))
        else None

    let getDevices (cardNumber: int) = [
        for number in [0 .. 9] do
            match tryReadFile $"/proc/asound/card{cardNumber}/pcm{number}c/info" with
            | None -> ()
            | Some text ->
                for line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries) do
                    if line.StartsWith("id: ") then {|
                        number = number
                        id = line.Substring(4)
                    |}
    ]

    let getCards () = [
        for number in [0 .. 9] do
            match tryReadFile $"/proc/asound/card{number}/id" with
            | None -> ()
            | Some id -> {|
                number = number
                id = id.Trim()
                devices = getDevices number
            |}
    ]
