using Microsoft.AspNetCore.Mvc;
using RadioHomeEngine.AspNetCore.Models;

namespace RadioHomeEngine.AspNetCore.Controllers
{
    public class CDUIController : Controller
    {
        public IActionResult Index()
        {
            return View(new CDsModel
            {
                CDs = Discovery.getDriveInfo(DiscDriveScope.AllDrives),
                Players = PlayerConnections.GetAll()
            });
        }

        [HttpPost]
        public async Task PlayCD(string device, string mac)
        {
            await AtomicActions.performActionAsync(
                LyrionCLI.Player.NewPlayer(mac),
                AtomicAction.NewPlayCD(
                    DiscDriveScope.NewSingleDrive(device)));
        }

        [HttpPost]
        public void RipCD(string device)
        {
            AtomicActions.beginRipAsync(
                DiscDriveScope.NewSingleDrive(device));
        }

        [HttpPost]
        public async Task EjectCD(string device)
        {
            await DiscDrives.ejectAsync(
                DiscDriveScope.NewSingleDrive(device));
        }
    }
}
