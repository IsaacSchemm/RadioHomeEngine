using Microsoft.AspNetCore.Mvc;
using System.Text;

namespace RadioHomeEngine.AspNetCore.Controllers
{
    public class StreamController : Controller
    {
        [Route("Stream/playlist.m3u8")]
        public IActionResult Playlist()
        {
            var contents = SegmentCache.getPlaylist();

            return Content(
                contents,
                "application/x-mpegURL",
                Encoding.UTF8);
        }

        [Route("Stream/chunklist.m3u8")]
        public IActionResult Chunklist()
        {
            var contents = SegmentCache.getChunklist();

            return Content(
                contents,
                "application/x-mpegURL",
                Encoding.UTF8);
        }

        [Route("Stream/chunk-{sequenceNumber}.ts")]
        public IActionResult Chunk(UInt128 sequenceNumber)
        {
            var data = SegmentCache.getData(sequenceNumber);

            return File(
                data,
                "video/mp2t");
        }
    }
}
