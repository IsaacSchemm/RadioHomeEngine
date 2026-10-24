namespace RadioHomeEngine

/// Information about a radio channel and what is playing on it.
type ProgramInformation = {
    channelName: string
    title: string
    artists: string list
    albums: string list
}
