using Microsoft.FSharp.Collections;

namespace RadioHomeEngine.AspNetCore.Models
{
    public record RecentlyPlayingModel
    {
        public required bool ShowPlayer { get; init; }
        public required PlayingChannelModel Channel { get; init; }
        public required FSharpList<SongModel> Songs { get; init; }
    }
}
