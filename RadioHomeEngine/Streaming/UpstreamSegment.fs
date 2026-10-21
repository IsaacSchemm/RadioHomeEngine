namespace RadioHomeEngine

open System

/// The type of encryption for an HLS media segment.
type UpstreamSegmentEncryption =
| Aes128 of uri: string
| Unknown
| Clear

/// An HLS media segment from a remote stream.
type UpstreamSegment = {
    encryption: UpstreamSegmentEncryption
    dateTime: DateTimeOffset option
    duration: float option
    mediaSequence: UInt128
    path: string
}
