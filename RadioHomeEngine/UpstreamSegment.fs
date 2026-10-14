namespace RadioHomeEngine

open System

type UpstreamSegmentEncryption =
| Aes128 of uri: string
| Unknown
| Clear

type UpstreamSegment = {
    encryption: UpstreamSegmentEncryption
    dateTime: DateTimeOffset option
    duration: float option
    mediaSequence: UInt128
    path: string
}
