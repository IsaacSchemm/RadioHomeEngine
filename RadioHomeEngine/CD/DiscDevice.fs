namespace RadioHomeEngine

open System
open System.Text

type DiscDevice = DiscDevice of string

module DiscDevice =
    let getPath dd =
        match dd with DiscDevice x -> x

    let getId dd =
        dd
        |> getPath
        |> Encoding.UTF8.GetBytes
        |> Convert.ToHexString

    let fromId (id: string) =
        id
        |> Convert.FromHexString
        |> Encoding.UTF8.GetString
        |> DiscDevice
