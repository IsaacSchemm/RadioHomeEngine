namespace RadioHomeEngine

open System
open System.Threading.Tasks

type UnresolvedRawAudioInput =
| Forecast
| PlayAudioCDs of DiscDriveScope
| PlayAudioCDTracks of DiscDriveScope
| ShuffleAudioCDTracks of DiscDriveScope
| ShuffleAudioFiles of files: string list

type RawAudioInput =
| AudioCD of DiscDrive
| AudioCDTrack of DiscDrive * AudioDiscInfo * trackNumber: int
| AudioFile of path: string
| BrownNoise of duration: RawAudioDuration
| Unresolved of UnresolvedRawAudioInput
| RemoteStream of IRemoteStreamSource
| Silence of duration: RawAudioDuration
| SpeechSynthesis of display: string * text: string
| WWVSimulator
