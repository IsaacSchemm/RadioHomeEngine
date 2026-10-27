using Microsoft.AspNetCore.Mvc;
using RadioHomeEngine.AspNetCore.Models;

namespace RadioHomeEngine.AspNetCore.Controllers
{
    public class RadioController(IHttpClientFactory httpClientFactory) : Controller
    {
        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            var channels = await StreamRepository.getAvailableChannelsAsync()
                .OrderBy(c => c.channelNumber)
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
            var availableChannel = await StreamRepository.getAvailableChannelsAsync()
                .Where(c => c.channelNumber == num)
                .FirstOrDefaultAsync(cancellationToken);

            if (availableChannel == null)
                return View("Blank");

            var activeChannel = StreamRepository.getCurrentChannels()
                .FirstOrDefault(c => c.ChannelNumber == num);

            var programInformation = activeChannel != null
                ? await activeChannel.GetCurrentStreamInformationAsync(cancellationToken)
                : null;

            return View(new NowPlayingModel
            {
                Channel = availableChannel,
                ProgramInformation = programInformation
            });
        }
    }
}
    