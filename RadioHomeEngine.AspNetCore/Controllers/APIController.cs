using Microsoft.AspNetCore.Mvc;
using Microsoft.FSharp.Collections;
using System.Runtime.CompilerServices;

namespace RadioHomeEngine.AspNetCore.Controllers
{
    [ApiController]
    [Route("api")]
    public class APIController : Controller
    {
        public record APISXMChannel(
            string ChannelNumber,
            string Name,
            FSharpList<string> ImageUrls);

        [HttpGet("sxm")]
        public async IAsyncEnumerable<APISXMChannel> GetSXMChannels(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var address = await Network.getAddressAsync();
            var channels = await SiriusXMClient.getChannelsAsync(cancellationToken);
            foreach (var channel in channels)
            {
                yield return new APISXMChannel(
                    channel.channelNumber,
                    channel.name,
                    [ .. channel.images.images
                        .Where(i => i.name == "color channel logo (on dark)")
                        .Where(i => i.width * 1.0 / i.height == 1.25)
                        .Select(i => i.url)
                    ]);
            }
        }

        [HttpGet("sxm/{channelNumber}")]
        public async Task<APISXMChannel> GetSXMChannel(
            string channelNumber,
            CancellationToken cancellationToken)
        =>
            await GetSXMChannels(cancellationToken)
            .Where(c => c.ChannelNumber == channelNumber)
            .FirstAsync(cancellationToken);

        public record APISXMSong(
            string Title,
            string Artist,
            APISXMAlbum? Album);

        public record APISXMAlbum(
            string Title,
            FSharpList<string> ImageUrls);

        [HttpGet("sxm/{channelNumber}/now-playing/songs")]
        public async IAsyncEnumerable<APISXMSong> GetSXMChannels(
            string channelNumber,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var channels = await SiriusXMClient.getChannelsAsync(cancellationToken);
            var channel = channels.First(c => c.channelNumber == channelNumber);

            var playlist = await SiriusXMClient.getPlaylistAsync(
                channel.channelGuid,
                channel.channelId,
                cancellationToken);

            foreach (var cut in playlist.cuts)
            {
                var album = cut.albums.HeadOrDefault;

                yield return new APISXMSong(
                    cut.title,
                    string.Join(" / ", cut.artists),
                    album == null
                        ? null
                        : new(
                            album.title,
                            album.images));
            }
        }

        public record APICDDrive(
            string Id,
            string DevicePath,
            APIAudioCD? AudioCD,
            APIDataCD? DataCD,
            bool Ripping);

        public record APIAudioCD(
            string AlbumName,
            string Artist,
            FSharpList<APIAudioCDTrack> Tracks);

        public record APIAudioCDTrack(
            int Number,
            string Title);

        public record APIDataCD(
            FSharpList<string> Files);

        private static APICDDrive ToAPICDDrive(DriveInfo drive) => new(
            Id: DiscDeviceModule.getId(drive.device),
            DevicePath: DiscDeviceModule.getPath(drive.device),
            AudioCD: drive.disc.AudioDiscs switch
            {
                [AudioDiscInfo adi] => new(
                    AlbumName: string.Join(" / ", adi.titles),
                    Artist: string.Join(" / ", adi.artists),
                    Tracks: [
                        .. adi.tracks.Select(t => new APIAudioCDTrack(
                            Number: t.position,
                            Title: t.title))
                    ]),
                _ => null
            },
            DataCD: drive.disc.DataDiscs switch
            {
                [DataDiscInfo ddi] => new(
                    Files: [
                        .. ddi.files.Select(f => f.name)
                    ]),
                _ => null
            },
            Ripping: Ripping.isCurrentlyRipping(drive.device));

        [HttpGet("cddrives")]
        public IEnumerable<APICDDrive> GetCDDrives()
        {
            foreach (var drive in Discovery.getDriveInfo(DiscDriveScope.AllDrives))
                yield return ToAPICDDrive(drive);
        }

        [HttpGet("cddrives/{driveId}")]
        public APICDDrive GetCDDrive(string driveId) =>
            Discovery.getDriveInfo(
                DiscDriveScope.NewSingleDrive(
                    DiscDeviceModule.fromId(driveId)))
            .Select(ToAPICDDrive)
            .Single();

        [HttpPost("cddrives/{driveId}/rip")]
        public ActionResult RipCD(string driveId)
        {
            Ripping.beginRip(
                DiscDriveScope.NewSingleDrive(
                    DiscDeviceModule.fromId(driveId)));

            return Accepted();
        }

        [HttpPost("cddrives/{driveId}/eject")]
        public async Task EjectCD(string driveId) =>
            await DiscDrives.ejectAsync(
                DiscDriveScope.NewSingleDrive(
                    DiscDeviceModule.fromId(driveId)));

        public record APIPlayer(
            string Id,
            string Name);

        [HttpGet("players")]
        public IEnumerable<APIPlayer> GetPlayers()
        {
            foreach (var p in PlayerConnections.GetAll())
                yield return new(
                    Id: p.MacAddress,
                    Name: p.Name);
        }

        [HttpGet("players/{playerId}")]
        public APIPlayer GetPlayer(string playerId) =>
            GetPlayers().Single(p => p.Id == playerId);

        private static PlaylistPosition GetPlaylistPosition(string parameterName) =>
            parameterName switch
            {
                "play" => PlaylistPosition.Now,
                "append" => PlaylistPosition.Last,
                _ => throw new ArgumentException(
                    "Invalid parameter name for \"play\" call",
                    nameof(parameterName))
            };

        [HttpPost("players/{playerId}/{playlistAction:regex(^(play|append)$)}/sxm/{channelNumber}")]
        public async Task PlaySXMChannel(string playerId, string playlistAction, int channelNumber) =>
            await AtomicActions.performActionAsync(
                LyrionCLI.Player.NewPlayer(playerId),
                AtomicAction.NewPlaySiriusXMChannel(
                    channelNumber,
                    GetPlaylistPosition(playlistAction)));

        [HttpPost("players/{playerId}/{playlistAction:regex(^(play|append)$)}/cddrives/{driveId}")]
        public async Task PlayCD(string playerId, string playlistAction, string driveId) =>
            await AtomicActions.performActionAsync(
                LyrionCLI.Player.NewPlayer(playerId),
                AtomicAction.NewPlayCD(
                    DiscDriveScope.NewSingleDrive(
                        DiscDeviceModule.fromId(driveId)),
                    GetPlaylistPosition(playlistAction)));

        [HttpPost("players/{playerId}/interrupt/forecast")]
        public async Task PlayForecast(string playerId) =>
            await AtomicActions.performActionAsync(
                LyrionCLI.Player.NewPlayer(playerId),
                AtomicAction.Forecast);
    }
}
