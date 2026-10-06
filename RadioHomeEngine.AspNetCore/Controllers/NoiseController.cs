using Microsoft.AspNetCore.Mvc;
using System.Text;

namespace RadioHomeEngine.AspNetCore.Controllers
{
    public class NoiseController : Controller
    {
        [Route("Noise/{filename}")]
        public async Task<IActionResult> Chunklist(string filename)
        {
            if (filename == "playlist.m3u8")
                return Content(
                    Noise.getPlaylist(),
                    "application/x-mpegURL",
                    Encoding.UTF8);

            Noise.init();

            var localPath = Path.Combine(Noise.path, filename);
            while (!System.IO.File.Exists(localPath))
            {
                await Task.Delay(1000);
            }

            return File(
                System.IO.File.ReadAllBytes(localPath),
                Path.GetExtension(localPath) switch {
                    ".m3u8" => "application/x-mpegURL",
                    ".ts" => "video/mp2t",
                    _ => throw new Exception("Unrecognized file extension")
                });
        }
    }
}
