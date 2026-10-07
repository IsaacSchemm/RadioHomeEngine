using RadioHomeEngine;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddHttpClient();

builder.Services.AddHostedService<NoiseGenerationService>();
//builder.Services.AddHostedService<LyrionCLIService>();
builder.Services.AddHostedService<WeatherService>();
builder.Services.AddHostedService<LyrionPlayerDetectionService>();
builder.Services.AddHostedService<DiscDriveChangeDetectionService>();

builder.Services.AddOpenApi();

var app = builder.Build();

app.MapOpenApi();
app.MapScalarApiReference();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run($"http://+:{Config.port}");
