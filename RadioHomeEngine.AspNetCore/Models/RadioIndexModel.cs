using Microsoft.FSharp.Collections;

namespace RadioHomeEngine.AspNetCore.Models
{
    public record RadioIndexModel
    {
        public required FSharpList<ChannelListing> Channels { get; init; }
        public required FSharpList<PlayerConnection> Players { get; init; }

        public int MinChannel => Channels
            .Select(c => c.ChannelNumber)
            .DefaultIfEmpty(0)
            .Min();

        public int MaxChannel => Channels
            .Select(c => c.ChannelNumber)
            .DefaultIfEmpty(0)
            .Max();
    }
}
