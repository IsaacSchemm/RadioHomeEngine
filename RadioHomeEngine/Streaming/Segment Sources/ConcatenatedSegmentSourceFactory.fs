namespace RadioHomeEngine

open System
open System.Diagnostics
open System.IO
open System.Threading.Tasks
open FSharp.Control
open System.Threading.Channels

type ConcatenatedSegmentSourceFactory(innerFactories: ISegmentSourceFactory list) =
    let mutable factoryStack = innerFactories
    let mutable currentSource = None

    interface ISegmentSourceFactory with
        member this.CreateAsync() = task {
            let mutable disposed = false

            let rec readNextAsync cancellationToken = task {
                // TODO: make this prettier

                //if factoryStack = [] then
                //    factoryStack <- innerFactories

                if currentSource = None && not disposed then
                    match factoryStack with
                    | first :: rest ->
                        let! source = first.CreateAsync()
                        currentSource <- Some source
                        factoryStack <- rest
                    | [] -> ()

                match currentSource with
                | None ->
                    return NoMoreSegments
                | Some source ->
                    let! result = source.ReadNextAsync(cancellationToken)
                    match result with
                    | NextSegment segment ->
                        return NextSegment segment
                    | NoMoreSegments ->
                        currentSource <- None
                        return! readNextAsync cancellationToken
            }

            return {
                new ISegmentSource with
                    member _.ProgramInformationSource
                        with get (): IProgramInformationSource = raise (NotImplementedException())

                    member _.ReadNextAsync(cancellationToken) = readNextAsync cancellationToken

                    member _.DisposeAsync() = ValueTask (task {
                        if not disposed then
                            disposed <- true

                        match currentSource with
                        | None -> ()
                        | Some source -> do! source.DisposeAsync()
                    })
            }
        }

        member this.ProgramInformationSource =
            match currentSource with
            | Some source -> source.ProgramInformationSource
            | None -> {
                new IProgramInformationSource with
                    member _.GetCurrentProgramInformationAsync(_) = Task.FromResult({
                        channelName = "None"
                        title = "None"
                        artists = []
                        albums = []
                    })
            }
