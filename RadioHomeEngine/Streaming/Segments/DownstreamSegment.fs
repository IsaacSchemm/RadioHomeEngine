namespace RadioHomeEngine

open System

/// An HLS media segment that has been downloaded or generated, decrypted (if necessary), and stored in memory.
type DownstreamSegment = {
    dateTime: DateTimeOffset
    duration: float
    data: byte array
    mediaSequence: UInt128
    important: bool
} with
    member this.BitsPerSecond =
        int (float (8 * this.data.Length) / this.duration)
