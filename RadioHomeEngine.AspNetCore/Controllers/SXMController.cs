using Microsoft.AspNetCore.Mvc;
using RadioHomeEngine.AspNetCore.Models;
using System.Text;

namespace RadioHomeEngine.AspNetCore.Controllers
{
    public class SXMController(IHttpClientFactory httpClientFactory) : Controller
    {
        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            var channelNumber = TunerProxy.getCurrentChannel();

            var channels = await SiriusXMClient.getChannelsAsync(cancellationToken);

            var model = new SiriusXMIndexModel
            {
                ChannelNumber = channelNumber,
                Channels = [
                    .. channels.Select(c => new SiriusXMIndexModel.Channel
                    {
                        ChannelNumber = int.Parse(c.channelNumber),
                        Name = c.name
                    })
                ]
            };

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> SetChannel(int channelNumber, CancellationToken cancellationToken)
        {
            await TunerProxy.setCurrentChannelAsync(channelNumber, cancellationToken);
            return RedirectToAction(nameof(ViewChannel));
        }

        [HttpPost]
        public async Task<IActionResult> ClearChannel(CancellationToken cancellationToken)
        {
            await TunerProxy.clearCurrentChannelAsync(cancellationToken);
            return RedirectToAction(nameof(ViewChannel));
        }

        public async Task<IActionResult> ChannelImage(int num, CancellationToken cancellationToken)
        {
            var channels = await SiriusXMClient.getChannelsAsync(cancellationToken);

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
            return Redirect($"/Proxy/playlist-{num}.m3u8");
        }

        public async Task<IActionResult> ViewChannel(CancellationToken cancellationToken)
        {
            var channels = await SiriusXMClient.getChannelsAsync(cancellationToken);
            var channel = channels
                .FirstOrDefault(c => c.channelNumber == $"{TunerProxy.getCurrentChannel()}");

            var history = await TunerProxy.getCurrentChannelHistoryAsync(cancellationToken);

            return View(new RecentlyPlayingModel
            {
                Channel = channel == null
                    ? null
                    : new PlayingChannelModel
                    {
                        Name = channel.name,
                        Number = channel.channelNumber,
                        Description = channel.mediumDescription
                    },
                Songs = [
                    .. history
                        .OrderByDescending(c => c.startTime)
                        .Take(5)
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
