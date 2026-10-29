using Microsoft.AspNetCore.Mvc;

namespace RadioHomeEngine.AspNetCore.Controllers
{
    public class SXMController : Controller
    {
        [Obsolete("This path was used by a previous version of the application.")]
        public async Task<IActionResult> PlayChannel(int num, CancellationToken cancellationToken)
        {
            return Redirect($"/Stream/playlist-{num}.m3u8");
        }
    }
}
