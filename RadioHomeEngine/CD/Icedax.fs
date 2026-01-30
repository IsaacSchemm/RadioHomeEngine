namespace RadioHomeEngine

open System
open System.Diagnostics
open System.IO
open System.Text
open System.Text.RegularExpressions
open System.Threading.Tasks

module Icedax =
    let albumTitlePattern = new Regex("^Album title: '(.*)")
    let audioTrackPattern = new Regex("^T([0-9]+): +[^ ]+ +[^ ]+ +audio .* title '(.*)")
    let dataTrackPattern = new Regex("^T([0-9]+): +[^ ]+ +[^ ]+ +data")
    let cdIndexPattern = new Regex("^CDINDEX discid: (.+)")
    let sampleFileSizePattern = new Regex("^samplefile size will be ([0-9]+) bytes")

    let unescapeTitle str = String [|
        use sr = new StringReader(str)

        let mutable finished = false
        while not finished do
            match sr.Read() with
            | -1 ->
                finished <- true
            | v when char v = '\'' ->
                finished <- true
            | v when char v = '\\' ->
                char (sr.Read())
            | v ->
                char v
    |]

    let (|AlbumTitle|_|) (str: string) = Seq.tryHead (seq {
        let m = albumTitlePattern.Match(str)
        if m.Success then
            unescapeTitle m.Groups[1].Value
    })

    let (|AudioTrack|_|) (str: string) = Seq.tryHead (seq {
        let m = audioTrackPattern.Match(str)
        if m.Success then
            let trackNumber = int m.Groups[1].Value
            let title = unescapeTitle m.Groups[2].Value
            (trackNumber, title)
    })

    let (|DataTrack|_|) (str: string) = Seq.tryHead (seq {
        let m = dataTrackPattern.Match(str)
        if m.Success then
            int m.Groups[1].Value
    })

    let (|CDINDEX|_|) (str: string) = Seq.tryHead (seq {
        let m = cdIndexPattern.Match(str)
        if m.Success then
            m.Groups[1].Value
    })

    let noDiscMessage = "load cdrom please and press enter"

    let getInfoAsync (device: string) = task {
        let proc =
            new ProcessStartInfo(
                "icedax",
                $"-J -g -D {device} -S 1 -v toc",
                RedirectStandardError = true,
                WorkingDirectory = "/tmp")
            |> Process.Start

        let _ = task {
            do! Task.Delay(10000)
            if not proc.HasExited then proc.Kill()
        }

        let! body = task {
            let body = new StringBuilder()

            use sr = proc.StandardError

            let mutable finished = false
            while not finished do
                match sr.Read() with
                | -1 ->
                    finished <- true
                | value ->
                    let c = char value
                    ignore (body.Append(c))

                    if body.ToString().EndsWith(noDiscMessage) then
                        proc.Kill()

            return body.ToString()
        }

        do! proc.WaitForExitAsync()

        if not proc.HasExited then proc.Kill()

        let mutable albumTitle = None
        let mutable tracks = []
        let mutable hasdata = false
        let mutable discid = None

        for line in Utility.split '\n' (body.ToString()) do
            match line with
            | AlbumTitle t when t <> "" ->
                albumTitle <- Some t
            | AudioTrack (n, t) ->
                tracks <- {| number = n; title = t |} :: tracks
            | DataTrack _ ->
                hasdata <- true
            | CDINDEX x ->
                discid <- Some x
            | _ -> ()

        return {|
            hasdata = hasdata
            disc = {
                discid = discid
                titles = Option.toList albumTitle
                artists = []
                tracks = [
                    for t in tracks |> Seq.sortBy (fun t -> t.number) do {
                        title = t.title
                        position = t.number
                    }
                ]
            }
        |}
    }

    let bytesPerSecond = 44100 * sizeof<uint16> * 2
    let sectorsPerSecond = 75
    let bytesPerSector = bytesPerSecond / sectorsPerSecond

    let extractWaveAsync (device: string) trackNumber skip = task {
        let spanString = $"-t {trackNumber}"

        let factor =
            let mutable bytes = skip
            let mutable sectors = 0

            while bytes > bytesPerSector + 1000 do
                bytes <- bytes - bytesPerSector
                sectors <- sectors + 1

            {|
                sectors = sectors
                bytes = bytes
            |}

        let proc =
            new ProcessStartInfo(
                "icedax",
                $"-D {device} {spanString} -S 1 -o {factor.sectors} -",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = "/tmp")
            |> Process.Start

        let! length = task {
            let tcs = new TaskCompletionSource<int64>()

            ignore (task {
                try
                    use sr = proc.StandardError
                    let mutable finished = false
                    while not finished do
                        let! line = sr.ReadLineAsync()
                        if isNull line then
                            finished <- true
                        else
                            let m = sampleFileSizePattern.Match(line)
                            if m.Success then
                                tcs.SetResult(Int64.Parse(m.Groups[1].Value))
                with ex ->
                    Console.Error.WriteLine(ex)
            })

            return! tcs.Task
        }

        let stream = proc.StandardOutput.BaseStream

        let buffer = Array.zeroCreate factor.bytes
        do! stream.ReadExactlyAsync(buffer.AsMemory())

        return {|
            stream = stream
            length = length + int64 (factor.sectors * bytesPerSector)
        |}
    }
