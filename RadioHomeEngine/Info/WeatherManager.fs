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
                let mutable players = []
                for playerData in PlayerConnections.GetAll() do
                    if playerData.PowerState then
                        let! mode = Playlist.getModeAsync playerData.Player
                        if mode = Playlist.Mode.Playing then
                            players <- playerData.Player :: players
                if not (List.isEmpty players) then
                    let! alerts = Weather.getNewAlertsAsync cancellationToken
                    if not (List.isEmpty alerts) then
                        for player in players do
                            do! Speech.readAsync player [for alert in alerts do alert.info]
            with ex -> Console.Error.WriteLine(ex)

            do! Task.Delay(TimeSpan.FromMinutes(5.0), cancellationToken)
    }
