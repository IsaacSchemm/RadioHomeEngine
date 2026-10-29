using Microsoft.AspNetCore.Mvc;
using RadioHomeEngine.AspNetCore.Models;

namespace RadioHomeEngine.AspNetCore.Controllers
{
    public class RadioController(IHttpClientFactory httpClientFactory) : Controller
    {
        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            var channels = await StreamRepository.getAvailableChannelsAsync()
                .OrderBy(c => c.ChannelNumber)
                .ToListAsync(cancellationToken);

            var model = new RadioIndexModel
            {
                Channels = [.. channels],
                Players = PlayerConnections.GetAll()
            };

            return View(model);
        }

        [HttpPost]
        public async Task PlayOn(int num, string macAddress)
        {
            var playerConnection = PlayerConnections.GetAll()
                .FirstOrDefault(player => player.MacAddress == macAddress);

            if (playerConnection != null)
                await AtomicActions.performActionAsync(
                    playerConnection.Player,
                    AtomicAction.NewPlayChannel(num, PlaylistPosition.Now));
        }

        public async Task<IActionResult> ChannelImage(int num, CancellationToken cancellationToken)
        {
            return NotFound();
        }

        public async Task<IActionResult> ViewChannel(int num, CancellationToken cancellationToken)
        {
            var activeChannel = StreamRepository.getOrCreate(num);
            if (activeChannel == null)
                return NotFound();

            await StreamRepository.waitForSegmentsAsync(num, cancellationToken);

            var programInformation = await activeChannel.GetCurrentStreamInformationAsync(cancellationToken);

            return View(new NowPlayingModel
            {
                Channel = activeChannel.ChannelListing,
                ProgramInformation = programInformation
            });
        }
    }
}
    