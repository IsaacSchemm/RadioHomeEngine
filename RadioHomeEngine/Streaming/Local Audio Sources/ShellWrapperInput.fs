namespace RadioHomeEngine

open System

type ShellWrapperInput =
| AudioCD of DiscDriveScope
| AudioFile of path: string
| BrownNoise of duration: ShellWrapperDuration
| Silence of duration: ShellWrapperDuration
| SpeechSynthesis of text: string
