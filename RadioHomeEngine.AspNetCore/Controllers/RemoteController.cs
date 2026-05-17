using Microsoft.AspNetCore.Mvc;

namespace RadioHomeEngine.AspNetCore.Controllers
{
    public class RemoteController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
