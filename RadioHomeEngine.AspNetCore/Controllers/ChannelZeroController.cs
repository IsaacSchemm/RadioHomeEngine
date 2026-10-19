using Microsoft.AspNetCore.Mvc;
using RadioHomeEngine.AspNetCore.Models;
using System.Text;

namespace RadioHomeEngine.AspNetCore.Controllers
{
    public class ChannelZeroController(IHttpClientFactory httpClientFactory) : Controller
    {
        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            var channelNumber = StreamRepository.getChannelZero();

            var channels = await SiriusXMClientModule.GetChannelsAsync(cancellationToken);

            var model = new SiriusXMIndexModel
            {
                ChannelNumber = channelNumber,
                Channels = [
                    .. channels.Select(c => new SiriusXMIndexModel.Channel
                    {
                        ChannelNumber = int.Parse(c.channelNumber),
                        Name = c.name
                    })
                ],
                Players = PlayerConnections.GetAll()
            };

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> SetChannel(int channelNumber)
        {
            await StreamRepository.setChannelZeroAsync(channelNumber);
            return RedirectToAction("ViewChannel", "SXM");
        }

        [HttpPost]
        public async Task<IActionResult> ClearChannel()
        {
            await StreamRepository.setChannelZeroAsync(null);
            return RedirectToAction("ViewChannel", "SXM");
        }

        [HttpPost]
        public async Task PlayOn(string macAddress)
        {
            var playerConnection = PlayerConnections.GetAll()
                .FirstOrDefault(player => player.MacAddress == macAddress);

            if (playerConnection != null)
                await AtomicActions.performActionAsync(
                    playerConnection.Player,
                    AtomicAction.NewPlayChannel(0, PlaylistPosition.Now));

            throw new NotImplementedException();
        }

        public ActionResult Player() => View();
    }
}
