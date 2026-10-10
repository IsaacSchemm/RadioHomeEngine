using Microsoft.AspNetCore.Mvc;

namespace RadioHomeEngine.AspNetCore.Controllers
{
    public class SilenceController : Controller
    {
        [Route("Silence/{filename}")]
        public async Task<IActionResult> GetFile(string filename, CancellationToken cancellationToken)
        {
            try
            {
                var contents = await Silence.getFileAsync(filename, cancellationToken);

                return contents == null
                    ? NotFound()
                    : File(
                        contents.data,
                        contents.contentType);
            }
            catch (Silence.InvalidFilenameException)
            {
                return NotFound();
            }
        }
    }
}
