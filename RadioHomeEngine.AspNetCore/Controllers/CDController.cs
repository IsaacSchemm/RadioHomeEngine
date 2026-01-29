using Microsoft.AspNetCore.Mvc;
using System.Text.RegularExpressions;

namespace RadioHomeEngine.AspNetCore.Controllers
{
    public partial class CDController() : Controller
    {
        public async Task<IActionResult> PlayTrack(string device, int track)
        {
            if (!DiscDrives.exists(device))
                return BadRequest();

            int offset = 0;

            if (Request.Headers.Range.SingleOrDefault() is string range
                && GetRangePattern().Match(range) is Match match
                && match.Success)
            {
                offset = int.Parse(match.Groups[1].Value);
            }

            var obj = await Icedax.extractWaveAsync(device, track, offset);

            Response.StatusCode = 200;
            Response.ContentType = "audio/wav";
            Response.ContentLength = obj.length;

            return File(
                obj.stream,
                "audio/wav",
                enableRangeProcessing: false);
        }

        public async Task<IActionResult> PlayFileFromCache(string device, int index)
        {
            if (!DiscDrives.exists(device))
                return BadRequest();

            int offset = 0;

            if (Request.Headers.Range.SingleOrDefault() is string range
                && GetRangePattern().Match(range) is Match match
                && match.Success)
            {
                offset = int.Parse(match.Groups[1].Value);

                throw new NotImplementedException($"offset: {offset}");
            }

            var disc = await DataCD.scanDeviceAsync(device);
            var stream = await DataCD.getStreamAsync(device, disc[index]);

            Response.StatusCode = 200;
            Response.ContentType = "audio/mpeg";
            Response.ContentLength = disc[index].size;

            return File(
                stream,
                "audio/mpeg",
                enableRangeProcessing: false);
        }

        [GeneratedRegex("^bytes=([0-9]+)-([0-9]+)$")]
        private static partial Regex GetRangePattern();
    }
}
