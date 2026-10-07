using Microsoft.AspNetCore.Mvc;

namespace RadioHomeEngine.AspNetCore.Controllers
{
    public class NoiseController : Controller
    {
        [Route("Noise/{filename}")]
        public async Task<IActionResult> GetFile(string filename)
        {
            var contents = NoiseGenerationServiceModule
                .GetFiles([filename])
                .SingleOrDefault();

            return contents == null
                ? NotFound()
                : File(
                    contents.data,
                    contents.contentType);
        }
    }
}
