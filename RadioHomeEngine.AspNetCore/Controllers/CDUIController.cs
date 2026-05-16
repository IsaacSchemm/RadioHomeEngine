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
        public async Task PlayCD(string id, string mac)
        {
            await AtomicActions.performActionAsync(
                LyrionCLI.Player.NewPlayer(mac),
                AtomicAction.NewPlayCD(
                    DiscDriveScope.NewSingleDrive(
                        DiscDevice.NewDiscDevice(id))));
        }

        [HttpPost]
        public void RipCD(string id)
        {
            AtomicActions.beginRipAsync(
                DiscDriveScope.NewSingleDrive(
                    DiscDevice.NewDiscDevice(id)));
        }

        [HttpPost]
        public async Task EjectCD(string id)
        {
            await DiscDrives.ejectAsync(
                DiscDriveScope.NewSingleDrive(
                    DiscDevice.NewDiscDevice(id)));
        }
    }
}
