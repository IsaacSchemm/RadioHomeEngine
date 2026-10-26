namespace RadioHomeEngine

open System
open System.Threading.Tasks
open Microsoft.Extensions.Hosting

open LyrionCLI

type WeatherService() =
    inherit BackgroundService()

    override _.ExecuteAsync cancellationToken = task {
        let! forecasts = Weather.getForecastsAsync cancellationToken
        do! Task.Delay(15000)
        let factory = new RawAudioSegmentSourceFactory(
            new ShellWrapperRawAudioFactory({
                inputs = [for f in forecasts |> Seq.truncate 3 do SpeechSynthesis (Speech.retrieveSpeech f.speech)]
                loop = false
            }))
        for stream in StreamRepository.getAll () do
            do! stream.InterruptAsync(factory)

        while not cancellationToken.IsCancellationRequested do
            try
                //let mutable players = []
                //for playerData in PlayerConnections.GetAll() do
                //    if playerData.PowerState then
                //        let! mode = Playlist.getModeAsync playerData.Player
                //        if mode = Playlist.Mode.Playing then
                //            players <- playerData.Player :: players
                //if not (List.isEmpty players) then
                //    let! alerts = Weather.getNewAlertsAsync cancellationToken
                //    if not (List.isEmpty alerts) then
                //        for player in players do
                //            do! Speech.readAsync player [for alert in alerts do alert.info]

                let streams = StreamRepository.getAll () |> Seq.toList
                if not (List.isEmpty streams) then
                    let! alerts = Weather.getNewAlertsAsync cancellationToken
                    if not (List.isEmpty alerts) then
                        let factory = new RawAudioSegmentSourceFactory(
                            new ShellWrapperRawAudioFactory({
                                inputs = [for f in forecasts do SpeechSynthesis (Speech.retrieveSpeech f.speech)]
                                loop = false
                            }))
                        for stream in streams do
                            do! stream.InterruptAsync(factory)
            with ex -> Console.Error.WriteLine(ex)

            do! Task.Delay(TimeSpan.FromMinutes(5.0), cancellationToken)
    }
