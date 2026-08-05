using DiceFight.Api;
using DiceFight.DiscordBot;

var builder = WebApplication.CreateBuilder(args);

// Cloud Run (and most container platforms) tell the container which port
// to listen on via $PORT. Only override the URL when it's actually set -
// otherwise local `dotnet run` keeps using launchSettings.json's port
// (5284) as normal, since PORT isn't part of a plain dev environment.
var port = Environment.GetEnvironmentVariable("PORT");
if (port is not null)
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});
builder.Services.AddSingleton<GameStore>();

// No-ops (logs a warning, doesn't start a gateway connection) unless
// DiscordBot:Token is configured - see DiscordBotService's own remarks.
// The Discord gateway connection this opens is long-lived, so whatever
// deploys this needs to be pinned to a single always-on instance rather
// than scaling to zero/many.
builder.Services.Configure<DiscordBotOptions>(builder.Configuration.GetSection("DiscordBot"));
builder.Services.AddHostedService<DiscordBotService>();

var app = builder.Build();

// Converts the engine's validation exceptions into HTTP responses instead
// of raw 500s - TurnEngine/CombatEngine throw InvalidOperationException
// liberally for illegal actions (wrong step, insufficient energy, etc.).
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (InvalidOperationException ex)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
    catch (KeyNotFoundException ex)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
});

app.MapControllers();

// Serves the React build (copied into wwwroot by the Dockerfile) and
// falls back to index.html for any route that isn't an API call or a real
// static file, so client-side routing works on a hard refresh/deep link.
// No CORS policy needed: the API and web client are served from the same
// origin in both this combined deployment and local dev (the Vite dev
// server proxies /api to this process instead of calling it cross-origin).
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

app.Run();
