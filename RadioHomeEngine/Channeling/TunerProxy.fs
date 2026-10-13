namespace RadioHomeEngine

open System
open System.Buffers.Binary
open System.Diagnostics
open System.IO
open System.Runtime.Caching
open System.Security.Cryptography
open System.Text
open System.Text.RegularExpressions
open System.Threading
open System.Threading.Tasks
open System.Net.Http

[<Obsolete>]
module TunerProxy =
    // Read individual media segments from whichever channel is current whenever the chunklist is updated,
    // and expose a single playlist and chunklist based on what we have cached.
    // (This sacrifices the adaptive streaming capability of HLS for the sake of simplicity.)

    // The playlist.m3u8 file must contain a bandwidth estimate for each chunklist.
    // We're only keeping one chunklist, so when we update it, let's update this value too.

    let mutable private bandwidth: int option = None

    // Prevent interference between different clients.
    // (Typically, there will be only one active listener to the stream, if any.)

    module private Lock =
        let flag = new SemaphoreSlim(1, 1)

        let doAsync (ct: CancellationToken) (f: unit -> Task<'T>) = task {
            do! flag.WaitAsync(ct)

            try
                return! f ()
            finally
                flag.Release() |> ignore
        }

    /// Gets a single segment's unencrypted audio data, using its new (client-facing) sequence number.
    /// If the segment is no longer cached, a segment that consists of ten seconds of silence will be returned instead.
    let getChunkAsync sequenceNumber cancellationToken = task {
        match SegmentCache.tryGetData sequenceNumber with
        | Some s -> return s
        | None -> return! Silence.generateSegmentAsync (TimeSpan.FromSeconds(10.0))
    }

    /// Checks the newest upstream chunklist, downloads any new segments, and builds a client-facing `chunklist.m3u8`.
    let getChunklistAsync cancellationToken = Lock.doAsync cancellationToken (fun () -> task {
        // Build the chunklist.

        return ChunklistParser.write (SegmentCache.list 3)
    })

    /// Builds a client-facing `playlist.m3u8`.
    let getPlaylist () = String.concat "\n" [
        "#EXTM3U"
        "#EXT-X-ALLOW-CACHE:NO"
        "#EXT-X-VERSION:1"

        String.concat "" [
            "#EXT-X-STREAM-INF:"

            match bandwidth with
            | Some bps -> $"BANDWIDTH={bps},"
            | None -> ()

            "CODECS=\"mp4a.40.2\""
        ]

        "chunklist.m3u8"
    ]
