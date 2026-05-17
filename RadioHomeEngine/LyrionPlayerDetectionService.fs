namespace RadioHomeEngine

open System
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open Nito.Disposables

open LyrionCLI

type LyrionPlayerDetectionService() =
    inherit BackgroundService()

    let processTelnetMessage command =
        try
            match command with
            | x :: "power" :: "0" :: _-> PlayerConnections.SetPowerState(Player x, false)
            | x :: "power" :: "1" :: _ -> PlayerConnections.SetPowerState(Player x, true)
            | _ -> ()
        with ex -> Console.Error.WriteLine(ex)

    override _.ExecuteAsync(cancellationToken) = task {
        use activeListeners = new CollectionDisposable()

        activeListeners.Add(LyrionCLI.subscribeToResponses processTelnetMessage)

        while not cancellationToken.IsCancellationRequested do
            try
                let! count = Players.countAsync ()
                for i in [0 .. count - 1] do
                    let! player = Players.getIdAsync i

                    let playerData =
                        match PlayerConnections.TryGet(player) with
                        | Some conn ->
                            conn
                        | None ->
                            let conn = new PlayerConnection(player)
                            PlayerConnections.Add(conn)
                            activeListeners.Add(new LyrionIRHandler(player))
                            conn

                    let! name = Players.getNameAsync player
                    playerData.Name <- name

                    let! state = Players.getPowerAsync player
                    playerData.PowerState <- state
            with ex ->
                Console.Error.WriteLine(ex)

            let waitFor =
                if PlayerConnections.GetAll() = []
                then TimeSpan.FromSeconds(15.0)
                else TimeSpan.FromMinutes(15.0)

            do! Task.Delay(waitFor, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing)
    }
