namespace RadioHomeEngine

type SegmentReadResult =
| NextSegment of DownstreamSegment
| NoMoreSegments
