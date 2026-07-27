using DiceFight.Api;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});
builder.Services.AddSingleton<GameStore>();

// Dev-only: lets the Vite dev server (a different origin) call this API.
// Tighten this before anything resembling production use.
const string devClientPolicy = "DevClient";
builder.Services.AddCors(options =>
{
    options.AddPolicy(devClientPolicy, policy => policy
        .WithOrigins("http://localhost:5173")
        .AllowAnyHeader()
        .AllowAnyMethod());
});

var app = builder.Build();

app.UseCors(devClientPolicy);

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

app.Run();
