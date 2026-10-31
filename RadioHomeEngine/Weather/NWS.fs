namespace RadioHomeEngine

open System
open System.IO
open System.Net.Http
open System.Runtime.Caching

module NWS =
    let USER_AGENT = Config.userAgentString
    let REST_BASE = "https://api.weather.gov/"

    let client =
        let cl = new HttpClient(BaseAddress = new Uri(REST_BASE))
        cl.DefaultRequestHeaders.Add("User-Agent", USER_AGENT)
        cl

    let nyc = (40.7m, -74.0m)

    let (latitude, longitude) =
        let filename = "location.txt"
        if filename |> File.Exists then
            match filename |> File.ReadAllText |> Utility.split ',' with
            | [| Decimal lat; Decimal lng |] -> lat, lng
            | _ -> nyc
        else
            nyc

    let tryCacheGet key =
        match MemoryCache.Default[key] with
        | :? 'T as item -> Some item
        | _ -> None

    let cacheKey = $"{Guid.NewGuid()}"

    let getPointAsync cancellationToken = task {
        match tryCacheGet cacheKey with
        | Some obj ->
            return obj
        | None ->
            use! resp = client.GetAsync(
                $"/points/{latitude},{longitude}",
                cancellationToken = cancellationToken)
            let! json = resp.EnsureSuccessStatusCode().Content.ReadAsStringAsync(cancellationToken)
            let obj = json |> Utility.deserializeAs {|
                properties = {|
                    forecast = ""
                    relativeLocation = {|
                        properties = {|
                            city = ""
                            state = ""
                            distance = {|
                                unitCode = ""
                                value = 0.0
                            |}
                        |}
                    |}
                |}
            |}
            MemoryCache.Default.Add(cacheKey, obj, DateTime.UtcNow.AddDays(1)) |> ignore
            return obj
    }

    let getForecastAsync cancellationToken = task {
        let! point = getPointAsync cancellationToken
        use! resp = client.GetAsync(
            point.properties.forecast,
            cancellationToken = cancellationToken)
        let! json = resp.EnsureSuccessStatusCode().Content.ReadAsStringAsync(cancellationToken)
        return json |> Utility.deserializeAs {|
            properties = {|
                generatedAt = DateTimeOffset.MinValue
                periods = [{|
                    name = ""
                    isDaytime = false
                    temperature = 0
                    temperatureUnit = ""
                    shortForecast = ""
                    detailedForecast = ""
                |}]
            |}
        |}
    }

    let getActiveAlertsAsync cancellationToken = task {
        use! resp = client.GetAsync(
            $"/alerts/active?point={latitude},{longitude}",
            cancellationToken = cancellationToken)
        let! json = resp.EnsureSuccessStatusCode().Content.ReadAsStringAsync(cancellationToken)
        return json |> Utility.deserializeAs {|
            features = [{|
                properties = {|
                    id = ""
                    sent = DateTimeOffset.MinValue
                    expires = DateTimeOffset.MinValue
                    headline = ""
                    description = ""
                    instruction = ""
                |}
            |}]
        |}
    }
