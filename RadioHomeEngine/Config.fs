namespace RadioHomeEngine

open System.IO

module Config =
    let port = 5000
    let userAgentString = "RadioHomeEngine/0.6 (https://github.com/IsaacSchemm/RadioHomeEngine)"

    let mediaDirectories = List.where Directory.Exists [
        "K:/Documents/Music"
        "/media/usb/Music"
    ]
