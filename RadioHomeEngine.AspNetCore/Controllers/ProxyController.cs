using Microsoft.AspNetCore.Mvc;
using System.Text;

namespace RadioHomeEngine.AspNetCore.Controllers
{
    public class ProxyController : Controller
    {
        [Route("Proxy/playlist-{channelNumber}.m3u8")]
        public IActionResult Playlist(int channelNumber, CancellationToken cancellationToken)
        {
            string contents = SegmentCache.getPlaylist();

            return Content(
                contents,
                "application/x-mpegURL",
                Encoding.UTF8);
        }

        [Route("Proxy/chunklist.m3u8")]
        public IActionResult Chunklist(CancellationToken cancellationToken)
        {
            string contents = SegmentCache.getChunklist();

            return Content(
                contents,
                "application/x-mpegURL",
                Encoding.UTF8);
        }

        [Route("Proxy/chunk-{sequenceNumber}.ts")]
        public IActionResult Chunk(UInt128 sequenceNumber, CancellationToken cancellationToken)
        {
            var data = SegmentCache.getData(sequenceNumber);

            return File(
                data,
                "video/mp2t");
        }
    }
}
