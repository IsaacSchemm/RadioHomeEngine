using Microsoft.AspNetCore.Mvc;
using RadioHomeEngine.AspNetCore.Models;

namespace RadioHomeEngine.AspNetCore.Controllers
{
    public class CDUIController : Controller
    {
        public IActionResult Index() =>
            View(new CDsModel
            {
                CDs = Discovery.getDriveInfo(DiscDriveScope.AllDrives),
                Players = PlayerConnections.GetAll()
            });

        [HttpPost]
        public async Task PlayCD(string id, string mac) =>
            await AtomicActions.performActionAsync(
                LyrionCLI.Player.NewPlayer(mac),
                AtomicAction.NewPlayCD(
                    DiscDriveScope.NewSingleDrive(
                        DiscDevice.NewDiscDevice(id)),
                    PlaylistPosition.Now));

        [HttpPost]
        public void RipCD(string id) =>
            Ripping.beginRip(
                DiscDriveScope.NewSingleDrive(
                    DiscDevice.NewDiscDevice(id)));

        [HttpPost]
        public void EjectCD(string id) =>
            Ripping.beginRip(
                DiscDriveScope.NewSingleDrive(
                    DiscDevice.NewDiscDevice(id)));
    }
}
