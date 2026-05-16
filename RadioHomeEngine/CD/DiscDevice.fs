namespace RadioHomeEngine

type DiscDevice = DiscDevice of string

module DiscDevice =
    let getId dd =
        match dd with DiscDevice x -> x

    let getPath dd =
        match dd with DiscDevice x -> x
