using RadioHomeEngine;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddHttpClient();

if (Environment.OSVersion.Platform == PlatformID.Unix)
{
    builder.Services.AddHostedService<LyrionCLIService>();
    builder.Services.AddHostedService<WeatherService>();
    builder.Services.AddHostedService<LyrionPlayerDetectionService>();
    builder.Services.AddHostedService<DiscDriveChangeDetectionService>();
}

builder.Services.AddHostedService<StreamRepository.Service>();

builder.Services.AddOpenApi();

builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);

var app = builder.Build();

app.MapOpenApi();
app.MapScalarApiReference();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run($"http://+:{Config.port}");
