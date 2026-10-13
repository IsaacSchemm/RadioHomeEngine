using Microsoft.AspNetCore.Mvc;
using System.Text;

namespace RadioHomeEngine.AspNetCore.Controllers
{
    public class ProxyController : Controller
    {
        [Route("Proxy/playlist-{channelNumber}.m3u8")]
        public async Task<IActionResult> Playlist(int channelNumber, CancellationToken cancellationToken)
        {
            string contents = TunerProxy.getPlaylist();

            return Content(
                contents,
                "application/x-mpegURL",
                Encoding.UTF8);
        }

        [Route("Proxy/chunklist.m3u8")]
        public async Task<IActionResult> Chunklist(CancellationToken cancellationToken)
        {
            string contents = await TunerProxy.getChunklistAsync(cancellationToken);

            return Content(
                contents,
                "application/x-mpegURL",
                Encoding.UTF8);
        }

        [Route("Proxy/chunk-{sequenceNumber}.ts")]
        public async Task<IActionResult> Chunk(UInt128 sequenceNumber, CancellationToken cancellationToken)
        {
            var data = await TunerProxy.getChunkAsync(sequenceNumber, cancellationToken);

            return File(
                data,
                "video/mp2t");
        }
    }
}
