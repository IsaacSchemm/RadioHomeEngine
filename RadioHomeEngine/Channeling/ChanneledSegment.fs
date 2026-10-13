namespace RadioHomeEngine

open System

type ChanneledSegment = {
    dateTime: DateTimeOffset
    duration: float
    data: byte array
}
