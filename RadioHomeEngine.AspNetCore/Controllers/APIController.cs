using Microsoft.AspNetCore.Mvc;
using Microsoft.FSharp.Collections;
using System.Runtime.CompilerServices;

namespace RadioHomeEngine.AspNetCore.Controllers
{
    [ApiController]
    [Route("api/v1")]
    public class APIController : Controller
    {
        public record APISXMImage(
            string Url);

        public record APISXMChannel(
            string ChannelNumber,
            string Name,
            APISXMImage? Image);

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
                    channel.images.images
                        .Where(i => i.name == "color channel logo (on dark)")
                        .Where(i => i.width * 1.0 / i.height == 1.25)
                        .Select(i => new APISXMImage(i.url))
                        .FirstOrDefault());
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
            APISXMAlbum? Album,
            DateTimeOffset StartTime);

        public record APISXMAlbum(
            string Title,
            APISXMImage? Image);

        [HttpGet("sxm/{channelNumber}/now-playing/history")]
        public async IAsyncEnumerable<APISXMSong> GetNowPlayingHistory(
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
                    cut.albums
                        .Select(album => new APISXMAlbum(
                            album.title,
                            album.images
                                .Select(i => new APISXMImage(i))
                                .FirstOrDefault()))
                        .FirstOrDefault(),
                    cut.startTime);
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

        private static async Task AddSXMChannelAsync(string playerId, PlaylistPosition playlistPosition, int channelNumber) =>
            await AtomicActions.performActionAsync(
                LyrionCLI.Player.NewPlayer(playerId),
                AtomicAction.NewPlaySiriusXMChannel(
                    channelNumber,
                    playlistPosition));

        [HttpPost("players/{playerId}/play/sxm/{channelNumber}")]
        public async Task PlaySXMChannel(string playerId, int channelNumber) =>
            await AddSXMChannelAsync(playerId, PlaylistPosition.Now, channelNumber);

        [HttpPost("players/{playerId}/append/sxm/{channelNumber}")]
        public async Task AppendSXMChannel(string playerId, int channelNumber) =>
            await AddSXMChannelAsync(playerId, PlaylistPosition.Last, channelNumber);

        private static async Task AddCDAsync(string playerId, PlaylistPosition playlistPosition, string driveId) =>
            await AtomicActions.performActionAsync(
                LyrionCLI.Player.NewPlayer(playerId),
                AtomicAction.NewPlayCD(
                    DiscDriveScope.NewSingleDrive(
                        DiscDeviceModule.fromId(driveId)),
                    playlistPosition));

        [HttpPost("players/{playerId}/play/cddrives/{driveId}")]
        public async Task PlaySXMChannel(string playerId, string driveId) =>
            await AddCDAsync(playerId, PlaylistPosition.Now, driveId);

        [HttpPost("players/{playerId}/append/cddrives/{driveId}")]
        public async Task AppendSXMChannel(string playerId, string driveId) =>
            await AddCDAsync(playerId, PlaylistPosition.Last, driveId);

        [HttpPost("players/{playerId}/play/forecast")]
        public async Task PlayForecast(string playerId) =>
            await AtomicActions.performActionAsync(
                LyrionCLI.Player.NewPlayer(playerId),
                AtomicAction.Forecast);
    }
}
