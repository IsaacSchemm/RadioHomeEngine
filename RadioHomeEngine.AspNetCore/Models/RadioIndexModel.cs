using Microsoft.FSharp.Collections;

namespace RadioHomeEngine.AspNetCore.Models
{
    public record RadioIndexModel
    {
        public required FSharpList<AvailableChannel> Channels { get; init; }
        public required FSharpList<PlayerConnection> Players { get; init; }

        public int MinChannel => Channels
            .Select(c => c.channelNumber)
            .DefaultIfEmpty(0)
            .Min();

        public int MaxChannel => Channels
            .Select(c => c.channelNumber)
            .DefaultIfEmpty(0)
            .Max();
    }
}
