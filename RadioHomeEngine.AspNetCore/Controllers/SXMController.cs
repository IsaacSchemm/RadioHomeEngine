using Microsoft.AspNetCore.Mvc;
using RadioHomeEngine.AspNetCore.Models;
using System.Text;

namespace RadioHomeEngine.AspNetCore.Controllers
{
    public class SXMController(IHttpClientFactory httpClientFactory) : Controller
    {
        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            var channels = await SiriusXMClientModule.GetChannelsAsync(cancellationToken);

            var model = new SiriusXMIndexModel
            {
                ChannelNumber = 4,
                Channels = [
                    .. channels.Select(c => new SiriusXMIndexModel.Channel
                    {
                        ChannelNumber = int.Parse(c.channelNumber),
                        Name = c.name
                    })
                ],
                Players = PlayerConnections.GetAll()
            };

            return View(model);
        }

        [HttpPost]
        public async Task PlayOn(int num, string macAddress)
        {
            var playerConnection = PlayerConnections.GetAll()
                .FirstOrDefault(player => player.MacAddress == macAddress);

            if (playerConnection != null)
                await AtomicActions.performActionAsync(
                    playerConnection.Player,
                    AtomicAction.NewPlayChannel(num, PlaylistPosition.Now));
        }

        public async Task<IActionResult> ChannelImage(int num, CancellationToken cancellationToken)
        {
            var channels = await SiriusXMClientModule.GetChannelsAsync(cancellationToken);

            var imageUrl = channels
                .Where(c => c.channelNumber == $"{num}")
                .SelectMany(c => c.images.images)
                .Where(i => i.name == "color channel logo (on dark)")
                .Where(i => i.width * 1.0 / i.height == 1.25)
                .Select(i => i.url)
                .FirstOrDefault();

            if (imageUrl == null)
            {
                return Content(
                    "<?xml version=\"1.0\"?><svg xmlns=\"http://www.w3.org/2000/svg\"/>",
                    "image/svg+xml",
                    Encoding.UTF8);
            }

            using var client = httpClientFactory.CreateClient();
            using var resp = await client.GetAsync(imageUrl, cancellationToken);
            var data = await resp.Content.ReadAsByteArrayAsync(cancellationToken);

            return File(
                data,
                resp.Content.Headers.ContentType?.MediaType ?? "application/octet-stream");
        }

        [Obsolete("This path was used by a previous version of the application.")]
        public async Task<IActionResult> PlayChannel(int num, CancellationToken cancellationToken)
        {
            return Redirect($"/Stream/playlist-{num}.m3u8");
        }

        public async Task<IActionResult> ViewChannel(int num, CancellationToken cancellationToken)
        {
            var channels = await SiriusXMClientModule.GetChannelsAsync(cancellationToken);
            var channel = channels
                .FirstOrDefault(c => c.channelNumber == $"{num}");

            if (channel == null)
                return View("Blank");

            var playlist = await SiriusXMClientModule.GetPlaylistAsync(
                num,
                cancellationToken);

            return View(new RecentlyPlayingModel
            {
                ShowPlayer = true,
                Channel = new PlayingChannelModel
                {
                    Name = channel.name,
                    Number = num,
                    Description = channel.mediumDescription
                },
                Songs = [
                    ..playlist.cuts
                        .OrderByDescending(c => c.startTime)
                        .Take(10)
                        .Select(c => new SongModel
                        {
                            Title = c.title,
                            Artist = string.Join(" / ", c.artists.Except([c.title])),
                            Album = c.albums.Select(a => a.title).FirstOrDefault(),
                            Image = c.albums.SelectMany(a => a.images).FirstOrDefault()
                        })
                ]
            });
        }
    }
}
