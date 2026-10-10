using Microsoft.AspNetCore.Mvc;

namespace RadioHomeEngine.AspNetCore.Controllers
{
    public class NoiseController : Controller
    {
        [Route("Noise/{filename}")]
        public async Task<IActionResult> GetFile(string filename)
        {
            try
            {
                var contents = Noise.getFile(filename);

                return contents == null
                    ? NotFound()
                    : File(
                        contents.data,
                        contents.contentType);
            }
            catch (Noise.InvalidFilenameException)
            {
                return NotFound();
            }
        }
    }
}
