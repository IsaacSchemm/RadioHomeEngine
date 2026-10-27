namespace RadioHomeEngine.AspNetCore.Models
{
    public record NowPlayingModel
    {
        public required AvailableChannel Channel { get; init; }
        public required ProgramInformation? ProgramInformation { get; init; }
    }
}
