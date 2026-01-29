using Microsoft.AspNetCore.Mvc;
using RadioHomeEngine.AspNetCore.Models;
using System.Threading.Tasks;

namespace RadioHomeEngine.AspNetCore.Controllers
{
    public class CDUIController : Controller
    {
        public async Task<IActionResult> Index()
        {
            var drives = await Discovery.getDriveInfoAsync(DiscDriveScope.AllDrives);

            return View(new CDsModel
            {
                CDs = [.. drives],
                Players = [
                    .. PlayerConnections.GetAll().Select(conn => new CDsModel.Player {
                        MacAddress = conn.MacAddress,
                        Name = conn.Name
                    })
                ]
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
