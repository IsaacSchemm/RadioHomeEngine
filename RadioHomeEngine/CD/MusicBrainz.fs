namespace RadioHomeEngine

open System
open System.Net
open System.Net.Http
open System.Text

module MusicBrainz =
    let private client =
        let c = new HttpClient()
        c.BaseAddress <- new Uri("https://musicbrainz.org/ws/2/")
        c.DefaultRequestHeaders.Accept.ParseAdd("application/json")
        c.DefaultRequestHeaders.UserAgent.ParseAdd(Config.userAgentString)
        c

    let private parseAs<'T> (_: 'T) (json: string) = Json.JsonSerializer.Deserialize<'T>(json)

    let getInfoAsync (discId: string) = task {
        use! discResponse = client.GetAsync($"discid/{discId}?inc=recordings+artist-credits")
        if discResponse.StatusCode = HttpStatusCode.NotFound then
            return None
        else
            let! discJson = discResponse.EnsureSuccessStatusCode().Content.ReadAsStringAsync()

            let disc = discJson |> parseAs {|
                releases = [{|
                    title = ""
                    media = [{|
                        tracks = [{|
                            title = ""
                            position = 0
                        |}]
                        discs = [{|
                            id = ""
                        |}]
                    |}]
                    ``artist-credit`` = [{|
                        name = ""
                    |}]
                    id = Some Guid.Empty
                |}]
            |}

            let release = Seq.head disc.releases

            return Some {
                discid = Some discId
                artists = [
                    for artist in release.``artist-credit`` do
                        artist.name
                ]
                titles = [release.title]
                tracks = [
                    let matchingMedia = Seq.tryHead (seq {
                        for m in release.media do
                            for d in m.discs do
                                if d.id = discId then m
                    })

                    let mediaList =
                        match matchingMedia with
                        | Some m ->
                            [m]
                        | None ->
                            Console.Error.WriteLine($"Found media, but not for disc ID {discId}")
                            release.media

                    for media in mediaList do
                        for track in media.tracks do {
                            title = track.title
                            position = track.position
                        }
                ]
            }
    }
