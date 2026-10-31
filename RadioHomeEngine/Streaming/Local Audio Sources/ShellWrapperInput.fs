namespace RadioHomeEngine

open System
open System.Threading.Tasks

type ShellWrapperInput =
| AudioCD of DiscDriveScope
| AudioFile of path: string
| BrownNoise of duration: ShellWrapperDuration
| Forecast
| RemoteStream of IRemoteStreamSource
| Silence of duration: ShellWrapperDuration
| SpeechSynthesis of display: string * text: string
| WWVSimulator
