using Microsoft.AspNetCore.Mvc;
using System.Text;

namespace RadioHomeEngine.AspNetCore.Controllers
{
    public class ProxyController : Controller
    {
        [Route("Proxy/playlist-{channelNumber}.m3u8")]
        public async Task<IActionResult> Playlist(int channelNumber, CancellationToken cancellationToken)
        {
            string contents = await MediaProxy.getPlaylistAsync(
                channelNumber,
                cancellationToken);

            return Content(
                contents,
                "application/x-mpegURL",
                Encoding.UTF8);
        }

        [Route("Proxy/chunklist-{channelNumber}-{index}.m3u8")]
        public async Task<IActionResult> Chunklist(int channelNumber, int index, CancellationToken cancellationToken)
        {
            string contents = await MediaProxy.getChunklistAsync(channelNumber, index, cancellationToken);

            return Content(
                contents,
                "application/x-mpegURL",
                Encoding.UTF8);
        }

        [Route("Proxy/chunk-{channelNumber}-{index}-{sequenceNumber}.ts")]
        public async Task<IActionResult> Chunk(int channelNumber, int index, UInt128 sequenceNumber, CancellationToken cancellationToken)
        {
            var data = await MediaProxy.getChunkAsync(channelNumber, index, sequenceNumber, cancellationToken);

            return File(
                data,
                "video/mp2t");
        }
    }
}
