namespace RadioHomeEngine

open System
open System.Diagnostics
open System.Text.RegularExpressions
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open System.Threading
open System.Text.Json

type DiscDriveChangeDetectionService() =
    inherit BackgroundService()

    let changePattern = new Regex("^UDEV +[^ ]+ +(change|remove)[ $]")

    let deserializeAs (_: 'T) (json: string) =
        JsonSerializer.Deserialize<'T>(json)

    let waitForMediaChangeAsync cancellationToken = task {
        use proc =
            new ProcessStartInfo(
                "udevadm",
                "monitor --udev",
                RedirectStandardOutput = true)
            |> Process.Start

        let changeDetectionTokenSource = new CancellationTokenSource()

        ignore (task {
            let cts = CancellationTokenSource.CreateLinkedTokenSource(
                changeDetectionTokenSource.Token,
                cancellationToken)
            do! Task.Delay(-1, cts.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing)
            proc.Kill()
        })

        use sr = proc.StandardOutput
        let mutable finished = false
        while not finished do
            let! line = sr.ReadLineAsync()
            if isNull line then
                finished <- true
            else if changePattern.IsMatch(line) then
                changeDetectionTokenSource.Cancel()
    }

    override _.ExecuteAsync(cancellationToken) = task {
        let udevAvailable =
            use proc = Process.Start("which", "udevadm")
            proc.WaitForExit()
            proc.ExitCode = 0

        while udevAvailable && not cancellationToken.IsCancellationRequested do
            try
                do! DiscDriveStatus.refreshAllAsync ()
                do! waitForMediaChangeAsync cancellationToken
            with ex ->
                Console.Error.WriteLine(ex)
                do! Task.Delay(TimeSpan.FromHours(1), cancellationToken)

        DiscDriveStatus.removeAll ()
    }
