namespace RadioHomeEngine

type RawAudioInput =
| AudioCDs of DiscDriveScope * AudioCDPlaybackMode
| AudioCDTrack of DiscDrive * AudioDiscInfo * trackNumber: int
| AudioFile of path: string
| BrownNoise of duration: RawAudioDuration
| Forecast
| RemoteStream of IRemoteStreamSource
| Shuffle of paths: string list
| Silence of duration: RawAudioDuration
| SpeechSynthesis of display: string * text: string
| WWVSimulator
