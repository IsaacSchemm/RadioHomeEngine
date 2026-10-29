namespace RadioHomeEngine.AspNetCore.Models
{
    public record NowPlayingModel
    {
        public required ChannelListing Channel { get; init; }
        public required ProgramInformation? ProgramInformation { get; init; }
    }
}
