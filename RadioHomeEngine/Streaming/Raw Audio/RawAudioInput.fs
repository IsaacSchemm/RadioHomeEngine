namespace RadioHomeEngine

type RawAudioInput =
| ALSA of card: int * device: int 
| AudioCD of DiscDriveScope
| AudioFile of path: string
| CD of DiscDriveScope * CDPlaybackMode
| CDTrack of DiscDrive * AudioDiscInfo * trackNumber: int
| BrownNoise of duration: RawAudioDuration
| Forecast
| RemoteStream of IRemoteStreamSource
| Shuffle of paths: string list
| Silence of duration: RawAudioDuration
| SpeechSynthesis of display: string * text: string
| WWVSimulator
