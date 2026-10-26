namespace RadioHomeEngine

open System
open System.IO
open System.Threading.Tasks

/// An audio source that provides a stream of CD-quality audio samples.
type IShellWrapperAudio =
    inherit IAsyncDisposable
    inherit IProgramInformationSource
    abstract member Stream: Stream
