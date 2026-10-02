using Microsoft.FSharp.Collections;

namespace RadioHomeEngine.AspNetCore.Models
{
    public record SiriusXMIndexModel
    {
        public required int? ChannelNumber { get; init; }
        public required FSharpList<Channel> Channels { get; init; }

        public record Channel
        {
            public required int ChannelNumber { get; init; }
            public required string Name { get; init; }
        }
    }
}
