namespace RadioHomeEngine

open System
open System.Diagnostics
open System.Threading.Tasks

/// An interface to the `abcde` audio CD ripping software.
module Abcde =
    /// Calculates the MusicBrainz disc ID for the disc inserted in the given drive.
    let getMusicBrainzDiscIdAsync (device: DiscDrive) = task {
        let proc =
            new ProcessStartInfo(
                "abcde-musicbrainz-tool",
                $"--command id --device {DiscDrive.getPath device}",
                RedirectStandardOutput = true)
            |> Process.Start

        let readTask = task {
            use sr = proc.StandardOutput
            let! output = sr.ReadToEndAsync()
            return output.Split(' ') |> Array.head
        }

        let! _ = Task.WhenAny(
            proc.WaitForExitAsync(),
            Task.Delay(TimeSpan.FromSeconds(15.0)))

        if not proc.HasExited then proc.Kill()

        let! id = readTask

        if String.IsNullOrEmpty(id)
        then return None
        else return Some id
    }

    /// Rips one or more audio CDs to the media directory used by Lyrion.
    let ripAsync scope = task {
        try
            let! dirs = LyrionCLI.General.getMediaDirsAsync()

            let dir =
                dirs
                |> Seq.tryHead
                |> Option.defaultWith (fun () -> failwith "No media_dir found to rip to")

            for device in DiscDrive.getDevices scope do
                let! info = Icedax.getInfoAsync device

                let trackString = String.concat " " [for t in info.disc.tracks do string t.position]

                let proc =
                    new ProcessStartInfo(
                        "abcde",
                        $"-a move,embedalbumart,clean -d {DiscDrive.getPath device} -o flac -f -N {trackString}",
                        WorkingDirectory = dir)
                    |> Process.Start

                do! proc.WaitForExitAsync()

            do! LyrionCLI.General.rescanAsync()
        with ex ->
            Console.Error.WriteLine(ex)
    }
