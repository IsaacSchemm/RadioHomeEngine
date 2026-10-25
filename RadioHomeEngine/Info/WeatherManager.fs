namespace RadioHomeEngine

open System
open System.Threading.Tasks
open Microsoft.Extensions.Hosting

open LyrionCLI

type WeatherService() =
    inherit BackgroundService()

    override _.ExecuteAsync cancellationToken = task {
        for _ in [0..2] do
            if DateTime.UtcNow < new DateTime(2026, 11, 1) then
                do! Task.Delay(15000)
                printfn "~~~~"
                let factory = new RawAudioSegmentSourceFactory(
                    new SpeechSynthesisAudioFactory("Testing", "Radio Home Engine Weather Service Test"))
                let activeStreams =
                   [ StreamRepository.getOrCreate 4 ]
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

                let activeStreams =
                    StreamRepository.getAll ()
                    |> Seq.where (fun a -> a.IsActive())
                    |> Seq.toList

                if activeStreams <> [] then
                    let! alerts = Weather.getNewAlertsAsync cancellationToken
                    for alert in alerts do
                        let factory = new SpeechSynthesisAudioFactory(alert.info.screen, Speech.retrieveSpeech alert.info.speech)
                        for stream in activeStreams do
                            do! stream.InterruptAsync(
                                new RawAudioSegmentSourceFactory(
                                    factory))
            with ex -> Console.Error.WriteLine(ex)

            do! Task.Delay(TimeSpan.FromMinutes(5.0), cancellationToken)
    }
