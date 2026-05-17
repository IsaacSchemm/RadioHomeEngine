namespace RadioHomeEngine

open System
open System.IO
open System.Net.Sockets
open System.Text
open System.Threading
open System.Threading.Tasks

open Microsoft.Extensions.Hosting

type LyrionCLIService() =
    inherit BackgroundService()

    let port = 9090
    let encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier = false)

    override _.ExecuteAsync(cancellationToken) = task {
        while not cancellationToken.IsCancellationRequested do
            let! ip = Network.getAddressAsync ()

            printfn $"Connecting to {ip}:{port}"

            let client = new TcpClient()

            let mutable connected = false
            while not connected do
                try
                    do! client.ConnectAsync(ip, port, cancellationToken)
                    connected <- true
                with :? SocketException as ex ->
                    printfn "%O" ex
                    do! Task.Delay(TimeSpan.FromSeconds(5.0), cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing)

            LyrionCLI.initialConnectionEstablished <- true

            use stream = client.GetStream()

            let telnet_to_event_task = task {
                try
                    use sr = new StreamReader(stream, Encoding.UTF8)

                    printfn $"Connected to port {port}"

                    do! LyrionCLI.sendCommandAsync ["subscribe"; "client,playlist,power,unknownir"]

                    let mutable finished = false
                    while client.Connected && not finished do
                        let! line = sr.ReadLineAsync(cancellationToken)
                        if isNull line then
                            client.Close()
                            finished <- true
                        else
                            let command =
                                line
                                |> Utility.split ' '
                                |> Seq.map Uri.UnescapeDataString
                                |> Seq.toList
                            LyrionCLI.broadcastResponse command
                with
                    | :? IOException when not client.Connected -> ()
                    | :? OperationCanceledException -> ()
            }

            let writeToken, cancelWrite =
                let cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                cts.Token, fun () -> cts.Cancel()

            let channel_to_telnet_task = task {
                try
                    use sw = new StreamWriter(stream, encoding, AutoFlush = true)

                    while not writeToken.IsCancellationRequested do
                        let! string = LyrionCLI.readNextCommandAsync writeToken
                        let sb = new StringBuilder(string)
                        do! sw.WriteLineAsync(sb, writeToken)
                with
                    | :? IOException when not client.Connected -> ()
                    | :? OperationCanceledException -> ()
            }

            do! telnet_to_event_task

            ignore channel_to_telnet_task

            printfn $"Disconnecting from port {port}"

            client.Close()

            printfn $"Disconnected from port {port}"
                
            cancelWrite ()

            do! Task.Delay(TimeSpan.FromSeconds(5.0), cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing)
    }
