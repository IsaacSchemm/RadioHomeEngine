namespace RadioHomeEngine

open System

type DownstreamSegment = {
    dateTime: DateTimeOffset
    duration: float
    data: byte array
    mediaSequence: UInt128
}

module DownstreamSegment =
    let tryCreate (segment: UpstreamSegment) (unencryptedData: byte array) =
        match (segment.dateTime, segment.duration) with
        | (Some dateTime, Some duration) ->
            Some {
                dateTime = dateTime
                duration = duration
                data = unencryptedData
                mediaSequence = segment.mediaSequence
            }
        | _ ->
            None
