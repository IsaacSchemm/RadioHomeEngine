namespace RadioHomeEngine

open System
open System.IO

/// An audio source that provides a stream of raw data which contains 64-bit
/// little-endian floating point PCM audio, with 2 channels at 44100 samples
/// per second.
type IRawAudio =
    inherit IAsyncDisposable
    abstract member Stream: Stream
