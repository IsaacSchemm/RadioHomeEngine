using RadioHomeEngine;
using Scalar.AspNetCore;

if (args.ContainsAny("--help", "/?"))
{
    Console.WriteLine($@"Usage: RadioHomeEngine [args]

Starts a server, accessible over unencrypted HTTP on port {Config.port}.

Available arguments:

    --lyrion
        Attempt to connect to Lyrion Media Server via telnet on localhost port {LyrionCLIServiceModule.port}.
        RadioHomeEngine can issue commands and listen to IR input on connected Squeezebox devices.

    --cd
        Use `udev` to monitor CD drives on the system.
        This enables ripping (to {Config.mediaDirectories.Head}), playback via Lyrion, and HLS streaming on channel {StreamRepository.playCD.ChannelListing.ChannelNumber}.

    --weather-alerts
        Enable monitoring of weather alerts from the National Weather Service.
        These alerts are converted into audio via `espeak` and are inserted into any active HLS streams.

Weather forecasts and alerts use the coordinates `latitude,longitude` in the file `location.txt`.

Additionally, if `username.txt` and `password.txt` are present, RadioHomeEngine will connect to SiriusXM and provide playable HLS streams for each live channel. (Xtra channels and on-demand content are not supported.)
");

    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddHttpClient();

if (args.Contains("--lyrion"))
{
    builder.Services.AddHostedService<LyrionCLIService>();
    builder.Services.AddHostedService<LyrionPlayerDetectionService>();
}

if (args.Contains("--cd"))
{
    builder.Services.AddHostedService<DiscDriveChangeDetectionService>();
}

if (args.Contains("--weather-alerts"))
{
    builder.Services.AddHostedService<WeatherAlertService>();
}

builder.Services.AddHostedService<StreamRepository.ChannelListingRefreshService>();
builder.Services.AddHostedService<StreamRepository.CleanupService>();

builder.Services.AddOpenApi();

builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);

var app = builder.Build();

app.MapOpenApi();
app.MapScalarApiReference();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run($"http://+:{Config.port}");
