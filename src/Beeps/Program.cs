using Beeps.Features.Localization;
using Beeps.Features.RateLimiting;
using Beeps.Features.Shared.Http;
using Beeps.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddBeepsProblemDetails();
builder.Services.AddBeepsLocalization();
builder.Services.AddBeepsRateLimiting();
builder.Services.AddBeepsInfrastructure();

var app = builder.Build();

// First, so everything after it (logs, rate limits, audit) sees the visitor's real IP.
app.UseForwardedHeaders();

// API callers get ProblemDetails; browsers get the error page.
app.UseWhen(context => context.Request.Path.StartsWithSegments("/api"), api =>
{
    api.UseExceptionHandler();
    api.UseStatusCodePages();
});
app.UseWhen(context => !context.Request.Path.StartsWithSegments("/api"), web =>
{
    web.UseExceptionHandler("/error");
    web.UseStatusCodePagesWithReExecute("/error/{0}");
});

app.UseRequestLocalization();
app.UseRouting();
app.UseMiddleware<RateLimitingMiddleware>();

app.MapStaticAssets().WithoutRateLimit();
app.MapRazorPages().WithStaticAssets();

app.Run();

/// <summary>Entry point; public so the integration tests can host the app.</summary>
public partial class Program;
