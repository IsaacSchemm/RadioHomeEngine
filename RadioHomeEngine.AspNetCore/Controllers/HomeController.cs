using Microsoft.AspNetCore.Mvc;

namespace RadioHomeEngine.AspNetCore.Controllers
{
    public class HomeController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }

        public IActionResult Player(int num)
        {
            return View(model: num);
        }
    }
}
