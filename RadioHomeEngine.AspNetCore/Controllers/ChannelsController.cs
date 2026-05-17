using Microsoft.AspNetCore.Mvc;
using Microsoft.FSharp.Collections;
using RadioHomeEngine.AspNetCore.Models;

namespace RadioHomeEngine.AspNetCore.Controllers
{
    public class ChannelsController : Controller
    {
        private static async Task<FSharpList<ChannelsModel.Channel>> GetChannelsAsync(CancellationToken cancellationToken)
        {
            FSharpList<ChannelsModel.Channel> channels = [];

            try
            {
                var list = await SiriusXMClient.getChannelsAsync(cancellationToken);

                return [
                    .. list.Select(c => new ChannelsModel.Channel
                    {
                        ChannelNumber = int.Parse(c.channelNumber),
                        Name = c.name
                    })
                ];
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return [];
            }
        }

        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            var channelsTask = GetChannelsAsync(cancellationToken);

            return View(new ChannelsModel
            {
                Channels = await channelsTask,
                Players = [
                    .. PlayerConnections.GetAll().Select(conn => new ChannelsModel.Player {
                        MacAddress = conn.MacAddress,
                        Name = conn.Name
                    })
                ]
            });
        }

        [HttpPost]
        public async Task Play(string mac, int num)
        {
            await AtomicActions.performActionAsync(
                LyrionCLI.Player.NewPlayer(mac),
                AtomicAction.NewPlaySiriusXMChannel(num, PlaylistPosition.Now));
        }

        [HttpPost]
        public async Task Clear(string mac)
        {
            await LyrionCLI.Playlist.clearAsync(
                LyrionCLI.Player.NewPlayer(mac));
        }
    }
}
