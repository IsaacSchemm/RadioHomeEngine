namespace RadioHomeEngine

open System
open System.Threading.Tasks
open Microsoft.Extensions.Hosting

open LyrionCLI

type WeatherService() =
    inherit BackgroundService()

    override _.ExecuteAsync cancellationToken = task {
        while not cancellationToken.IsCancellationRequested do
            try
                let streams = StreamRepository.getCurrentChannels () |> Seq.toList
                if not (List.isEmpty streams) then
                    let! alerts = Weather.getNewAlertsAsync cancellationToken
                    if not (List.isEmpty alerts) then
                        let factory = new ShellSegmentSourceFactory(
                            "Weather",
                            [for a in alerts do SpeechSynthesis (a.info.screen, Speech.retrieveSpeech a.info.speech)])
                        for stream in streams do
                            if stream.IsActive() then
                                do! stream.InterruptAsync(factory)
            with ex -> Console.Error.WriteLine(ex)

            do! Task.Delay(TimeSpan.FromMinutes(5.0), cancellationToken)
    }
