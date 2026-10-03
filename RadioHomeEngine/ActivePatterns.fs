namespace RadioHomeEngine

open System

[<AutoOpen>]
module ActivePatterns =
    let (|Decimal|_|) (str: string) =
        match Decimal.TryParse(str) with
        | true, value -> Some value
        | false, _ -> None

    let (|Int32|_|) (str: string) =
        match Int32.TryParse(str) with
        | true, value -> Some value
        | false, _ -> None

    let (|UInt128|_|) (str: string) =
        match UInt128.TryParse(str) with
        | true, value -> Some value
        | false, _ -> None

    let (|DateTimeOffset|_|) (str: string) =
        match DateTimeOffset.TryParse(str) with
        | true, value -> Some value
        | false, _ -> None
