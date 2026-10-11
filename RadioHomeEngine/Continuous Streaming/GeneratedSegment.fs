namespace RadioHomeEngine

open System

type GeneratedSegment = {
    dateTime: DateTimeOffset
    duration: float
    data: byte array
    originalPath: string
}
