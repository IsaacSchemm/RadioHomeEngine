namespace RadioHomeEngine

open System
open System.Text.RegularExpressions

[<AutoOpen>]
module ActivePatterns =
    let (|CommaSeparated|_|) (str: string) =
        str.Split(',')
        |> List.ofArray
        |> Some

    let (|HLSAttribute|_|) (str: string) =
        match str.Split('=') with
        | [| key; value |] -> Some (key, value)
        | _ -> None

    let (|HLSQuotedString|_|) (str: string) =
        let expression = new Regex("""^"([^"\r\n]*)"$""")
        let ``match`` = expression.Match(str)
        if ``match``.Success then
            Some ``match``.Groups.[1].Value
        else
            None

    let (|Decimal|_|) (str: string) =
        match Decimal.TryParse(str) with
        | true, value -> Some value
        | false, _ -> None

    let (|Float64|_|) (str: string) =
        match Double.TryParse(str) with
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
