using Microsoft.AspNetCore.Mvc;
using System.Text;

namespace RadioHomeEngine.AspNetCore.Controllers
{
    public class StreamController : Controller
    {
        [Route("Stream/playlist-{channelNumber}.m3u8")]
        public async Task<IActionResult> Playlist(int channelNumber, CancellationToken cancellationToken)
        {
            var contents = StreamRepository.getPlaylist(channelNumber);
            await StreamRepository.waitForSegmentsAsync(channelNumber, cancellationToken);

            return Content(
                contents,
                "application/x-mpegURL",
                Encoding.UTF8);
        }

        [Route("Stream/chunklist-{channelNumber}.m3u8")]
        public async Task<IActionResult> Chunklist(int channelNumber, CancellationToken cancellationToken)
        {
            var contents = StreamRepository.getChunklist(channelNumber);
            await StreamRepository.waitForSegmentsAsync(channelNumber, cancellationToken);

            return Content(
                contents,
                "application/x-mpegURL",
                Encoding.UTF8);
        }

        [Route("Stream/chunk-{channelNumber}-{sequenceNumber}.ts")]
        public IActionResult Chunk(int channelNumber, UInt128 sequenceNumber)
        {
            var data = StreamRepository.getChunk(channelNumber, sequenceNumber);

            return File(
                data,
                "video/mp2t");
        }
    }
}
