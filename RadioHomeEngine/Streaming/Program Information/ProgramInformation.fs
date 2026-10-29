namespace RadioHomeEngine

/// Information about a radio channel and what is playing on it.
type ProgramInformation = {
    ChannelName: string
    Program: Program
} with
    member this.ProgramName =
        match this.Program with
        | Program str -> str
        | Song song -> String.concat " - " [
            if song.Artists <> [] then
                String.concat " / " song.Artists
            song.Title
        ]
