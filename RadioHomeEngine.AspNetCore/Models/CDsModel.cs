using Microsoft.FSharp.Collections;

namespace RadioHomeEngine.AspNetCore.Models
{
    public record CDsModel
    {
        public required FSharpList<DriveInfo> CDs { get; init; }
        public required FSharpList<PlayerConnection> Players { get; init; }
    }
}
