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

// Serves the React build (copied into wwwroot by the Dockerfile), plus any
// other static subtree in wwwroot (e.g. /alpha for the v3 prototype), and
// falls back to index.html for any route that isn't an API call or a real
// static file, so client-side routing works on a hard refresh/deep link.
// No CORS policy needed: the API and web client are served from the same
// origin in both this combined deployment and local dev (the Vite dev
// server proxies /api to this process instead of calling it cross-origin).
//
// The explicit UseRouting() below is load-bearing, not decorative. Without
// it, ASP.NET Core auto-inserts routing at the very START of the pipeline
// the moment ANY Map*() call exists anywhere in this file - regardless of
// where that Map*() call is textually written, so moving UseStaticFiles
// above MapControllers()/MapFallbackToFile() (tried first, didn't help)
// changes nothing. That auto-inserted routing matches MapFallbackToFile's
// endpoint against every extension-less path (by design - a real static
// file WITH an extension, e.g. /alpha/index.html, was never affected,
// which is why only extension-less nested paths broke). Once an endpoint
// is matched, StaticFileMiddleware/DefaultFilesMiddleware deliberately
// defer to it and skip serving even when the real file exists - so /alpha
// and /alpha/ silently fell through to the SPA's own root index.html.
// Calling UseRouting() explicitly HERE, after static files, makes routing
// run only for requests static files didn't already short-circuit -
// confirmed against a minimal repro before touching this file for real.
// Found 2026-09-03 verifying the v3 prototype's /alpha deploy.
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRouting();

// Converts the engine's validation exceptions into HTTP responses instead
// of raw 500s - TurnEngine/CombatEngine throw InvalidOperationException
// liberally for illegal actions (wrong step, insufficient energy, etc.).
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    // Seat problems are their own thing: 401 means "you did not prove
    // which side you are", 403 means "you did, and it is not your move" -
    // a distinction the client needs, since only the first is worth
    // asking the player to re-open their invite link over.
    catch (SeatRequiredException ex)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
    catch (NotYourTurnException ex)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
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
app.MapFallbackToFile("index.html");

app.Run();
