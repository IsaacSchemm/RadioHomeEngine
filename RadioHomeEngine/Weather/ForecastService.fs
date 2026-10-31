namespace RadioHomeEngine

open System
open System.Threading.Tasks
open Microsoft.Extensions.Hosting

module ForecastService =
    let mutable private forecast: Weather.Readable list = []

    let mutable private lastAccessed = DateTimeOffset.MinValue
    let mutable private lastUpdated = DateTimeOffset.MinValue

    let getTitle () =
        lastAccessed <- DateTimeOffset.UtcNow

        forecast
        |> Seq.map (fun f -> f.screen)
        |> Seq.tryHead
        |> Option.defaultValue "Forecast"

    let getBody () =
        lastAccessed <- DateTimeOffset.UtcNow

        match forecast with
        | [] ->
            "No forecast available"
        | _ ->
            forecast
            |> Seq.map (fun f -> f.speech)
            |> String.concat Environment.NewLine

    let set newForecast =
        lastAccessed <- DateTimeOffset.UtcNow
        lastUpdated <- DateTimeOffset.UtcNow
        forecast <- newForecast

    let usedRecently () =
        DateTimeOffset.UtcNow - lastAccessed < TimeSpan.FromMinutes(5.0)

    let isOld () =
        DateTimeOffset.UtcNow - lastUpdated > TimeSpan.FromHours(4.0)

type ForecastService() =
    inherit BackgroundService()

    override _.ExecuteAsync cancellationToken = task {
        while not cancellationToken.IsCancellationRequested do
            if ForecastService.usedRecently () || ForecastService.isOld () then
                try
                    let! forecasts = Weather.getForecastsAsync cancellationToken
                    ForecastService.set (List.take 3 forecasts)
                with ex ->
                    Console.Error.WriteLine(ex)
                    ForecastService.set ([])

            do! Task.Delay(TimeSpan.FromMinutes(5.0), cancellationToken)
    }
