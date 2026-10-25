namespace RadioHomeEngine

open System
open System.IO

/// An audio source that provides a stream of CD-quality audio samples.
type IRawAudio =
    inherit IAsyncDisposable
    abstract member Stream: Stream
