using Microsoft.AspNetCore.Mvc;

namespace RadioHomeEngine.AspNetCore.Controllers
{
    public class NoiseController : Controller
    {
        [Route("Noise/{filename}")]
        public async Task<IActionResult> GetFile(string filename, CancellationToken cancellationToken)
        {
            try
            {
                var contents = await Noise.getFileAsync(filename, cancellationToken);

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
