namespace RadioHomeEngine

/// Information about a program playing on a radio channel.
type Program =
| Program of string
| Song of Song
