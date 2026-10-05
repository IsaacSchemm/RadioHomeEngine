using Microsoft.AspNetCore.Mvc;
using System.Text;

namespace RadioHomeEngine.AspNetCore.Controllers
{
    public class NoiseController : Controller
    {
        [Route("Noise")]
        public IActionResult Index()
        {
            return View();
        }

        [Route("Noise/playlist.m3u8")]
        public IActionResult Playlist()
        {
            return Content(
                Noise.getPlaylist(),
                "application/x-mpegURL",
                Encoding.UTF8);
        }

        [Route("Noise/chunklist.m3u8")]
        public IActionResult Chunklist()
        {
            return Content(
                Noise.getChunklist(),
                "application/x-mpegURL",
                Encoding.UTF8);
        }

        [Route("Noise/chunk-{_}.ts")]
        public async Task<IActionResult> Chunk(long _, CancellationToken cancellationToken)
        {
            return File(
                await Noise.getChunkAsync(cancellationToken),
                "video/mp2t");
        }
    }
}
