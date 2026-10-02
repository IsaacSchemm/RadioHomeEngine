namespace RadioHomeEngine.AspNetCore.Models
{
    public record PlayingChannelModel
    {
        public required string Name { get; init; }
        public required string Number { get; init; }
        public required string Description { get; init; }
    }
}
