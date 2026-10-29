namespace RadioHomeEngine

open System
open System.Buffers.Binary
open System.Diagnostics
open System.IO
open System.Net
open System.Net.Http
open System.Security.Cryptography
open System.Text
open System.Threading
open System.Threading.Channels
open System.Threading.Tasks

module private UnauthenticatedHLSSourceFactory =
    let handler = new HttpClientHandler()
    handler.AutomaticDecompression <- DecompressionMethods.GZip ||| DecompressionMethods.Deflate

    let client = new HttpClient(handler)

/// A media source that proxies an unencrypted HLS stream.
type UnauthenticatedHLSSourceFactory(name: string, url: string) =
    inherit HLSSourceFactory()

    override this.ChannelInformation = {
        ChannelName = name
        ChannelDescriptions = []
    }

    override this.ProgramInformationSource = {
        new IProgramInformationSource with
            member _.GetCurrentProgramInformationAsync(cancellationToken) = task {
                return {
                    ChannelName = name
                    Program = Program name
                }
            }
    }

    override this.DecryptSegmentAsync(segment, data) = task {
        match segment.encryption with
        | Clear -> return data
        | x -> return failwithf "Unsupported encryption type %A" x
    }

    override this.GetFileAsync(uri, cancellationToken) = task {
        use! finalResponse = UnauthenticatedHLSSourceFactory.client.GetAsync(
            uri,
            cancellationToken)

        use! stream = finalResponse.EnsureSuccessStatusCode().Content.ReadAsStreamAsync(cancellationToken)

        use ms = new MemoryStream()
        do! stream.CopyToAsync(ms)
        let data = ms.ToArray()

        return data
    }

    override this.GetPlaylistUriAsync() =
        Task.FromResult(new Uri(url))

    override this.GetTextFileAsync(uri, cancellationToken) = task {
        use! finalResponse = UnauthenticatedHLSSourceFactory.client.GetAsync(
            uri,
            cancellationToken)

        let! text = finalResponse.EnsureSuccessStatusCode().Content.ReadAsStringAsync(cancellationToken)

        return text
    }
