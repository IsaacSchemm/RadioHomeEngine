using Microsoft.AspNetCore.Mvc;
using RadioHomeEngine.AspNetCore.Models;

namespace RadioHomeEngine.AspNetCore.Controllers
{
    public class RadioController : Controller
    {
        public IActionResult Index()
        {
            var channels = StreamRepository.getCurrentChannels()
                .Select(c => c.ChannelListing)
                .OrderBy(c => c.ChannelNumber)
                .ToList();

            var model = new RadioIndexModel
            {
                Channels = [.. channels]
            };

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> PlayOn(int num, string macAddress)
        {
            var playerConnection = PlayerConnections.GetAll()
                .FirstOrDefault(player => player.MacAddress == macAddress);

            if (playerConnection != null)
                await AtomicActions.performActionAsync(
                    playerConnection.Player,
                    AtomicAction.NewPlayChannel(num, PlaylistPosition.Now));

            return Accepted();
        }

        public async Task<IActionResult> ViewChannel(int num, CancellationToken cancellationToken)
        {
            var info = await StreamRepository.getChannelListingAndProgramInformationAsync(num, cancellationToken);

            await StreamRepository.waitForSegmentsAsync(num, cancellationToken);

            return View(new NowPlayingModel
            {
                Channel = info.channelListing,
                ProgramInformation = info.programInformation,
                Players = PlayerConnections.GetAll()
            });
        }
    }
}
