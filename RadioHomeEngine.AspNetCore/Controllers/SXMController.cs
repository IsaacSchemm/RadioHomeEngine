using Microsoft.AspNetCore.Mvc;
using RadioHomeEngine.AspNetCore.Models;
using System.Text;

namespace RadioHomeEngine.AspNetCore.Controllers
{
    public class SXMController(IHttpClientFactory httpClientFactory) : Controller
    {
        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            var channelNumber = StreamRepository.getChannelZero();

            var channels = await SiriusXMClientModule.GetChannelsAsync(cancellationToken);

            var model = new SiriusXMIndexModel
            {
                ChannelNumber = channelNumber,
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
        public async Task<IActionResult> SetChannel(int channelNumber)
        {
            await StreamRepository.setChannelZeroAsync(channelNumber);
            return RedirectToAction(nameof(ViewChannel));
        }

        [HttpPost]
        public async Task<IActionResult> ClearChannel()
        {
            await StreamRepository.setChannelZeroAsync(null);
            return RedirectToAction(nameof(ViewChannel));
        }

        [HttpPost]
        public async Task PlayOn(string macAddress, CancellationToken cancellationToken)
        {
            var playerConnection = PlayerConnections.GetAll()
                .FirstOrDefault(player => player.MacAddress == macAddress);

            //if (playerConnection != null)
            //    await AtomicActions.performActionAsync(
            //        playerConnection.Player,
            //        AtomicAction.PlayCurrentChannel);

            throw new NotImplementedException();
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

        [Obsolete]
        public async Task<IActionResult> PlayChannel(int num, CancellationToken cancellationToken)
        {
            return Redirect($"/Stream/playlist-{num}.m3u8");
        }

        public async Task<IActionResult> ViewChannel(int? num, CancellationToken cancellationToken)
        {
            if ((num ?? StreamRepository.getChannelZero()) is not int channelNumber)
                return NotFound();

            var channels = await SiriusXMClientModule.GetChannelsAsync(cancellationToken);
            var channel = channels
                .Where(c => c.channelNumber == $"{channelNumber}")
                .First();

            var playlist = await SiriusXMClientModule.GetPlaylistAsync(
                channelNumber,
                cancellationToken);

            return View(new RecentlyPlayingModel
            {
                ShowPlayer = num != null,
                Channel = new PlayingChannelModel
                {
                    Name = channel.name,
                    Number = channelNumber,
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

        public ActionResult Player() => View();
    }
}
