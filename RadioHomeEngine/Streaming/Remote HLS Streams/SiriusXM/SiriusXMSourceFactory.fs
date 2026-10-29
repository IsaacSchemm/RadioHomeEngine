namespace RadioHomeEngine

open System
open System.Buffers.Binary
open System.Diagnostics
open System.IO
open System.Security.Cryptography
open System.Text
open System.Threading
open System.Threading.Channels
open System.Threading.Tasks

/// A media source that decrypts and proxies a SiriusXM stream.
type SiriusXMSourceFactory(channelNumber: int, initialCount: int) =
    inherit HLSSourceFactory()

    let sxm = new SiriusXMClient()

    let mutable channelDescriptions = []
    let mutable channelName = None

    interface IProgramInformationSource with
        member _.GetCurrentProgramInformationAsync(cancellationToken) = task {
            let! channels = sxm.GetChannelsAsync(cancellationToken)

            let channel =
                channels
                |> Seq.where (fun c -> c.channelNumber = $"{channelNumber}")
                |> Seq.tryHead

            let! playlist = sxm.GetPlaylistAsync(channelNumber, CancellationToken.None)

            let cut =
                playlist.cuts
                |> Seq.sortByDescending (fun cut -> cut.startTime)
                |> Seq.tryHead

            match (channel, cut) with
            | Some ch, Some s ->
                return {
                    ChannelName = ch.name
                    Program = Song {
                        Title = s.title
                        Artists = s.artists
                        Albums = [for a in s.albums do a.title]
                        Images = [for a in s.albums do yield! a.images]
                    }
                }
            | _ ->
                return {
                    ChannelName = "Unknown"
                    Program = Program "Unknown"
                }
        }

    override this.ChannelInformation = {
        ChannelName =
            channelName
            |> Option.defaultValue $"SiriusXM {channelNumber}"
        ChannelDescriptions = channelDescriptions
    }

    override this.ProgramInformationSource =
        this

    override this.DecryptSegmentAsync(chunk, data) = task {
        match chunk.encryption with
        | Aes128 "key/1" ->
            use algorithm = Aes.Create()
            algorithm.Padding <- PaddingMode.PKCS7
            algorithm.Mode <- CipherMode.CBC
            algorithm.KeySize <- 128
            algorithm.BlockSize <- 128

            algorithm.Key <- sxm.GetKey()

            algorithm.IV <-
                let iv = Array.zeroCreate 16
                BinaryPrimitives.WriteUInt128BigEndian(iv.AsSpan(), chunk.mediaSequence)
                iv

            use outputStream = new MemoryStream()

            do! task {
                use cryptoStream = new CryptoStream(outputStream, algorithm.CreateDecryptor(), CryptoStreamMode.Write)
                do! cryptoStream.WriteAsync(data)
            }

            return outputStream.ToArray()
        | Clear ->
            return data
        | x ->
            return failwithf "Unrecognized stream encryption: %A" x
    }

    override this.GetFileAsync(uri, cancellationToken) = task {
        let! data = sxm.GetFileAsync(uri, cancellationToken)
        return data.content
    }

    override this.GetPlaylistUriAsync() = task {
        let! playlist = sxm.GetPlaylistAsync(channelNumber, CancellationToken.None)
        return new Uri(playlist.url)
    }

    override this.GetTextFileAsync(uri, cancellationToken) = task {
        let! data = sxm.GetFileAsync(uri, cancellationToken)
        return Encoding.UTF8.GetString(data.content)
    }
