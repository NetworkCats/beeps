using Beeps.Features;
using Beeps.Features.Agents;
using Beeps.Features.Auth;
using Beeps.Features.Avatars;
using Beeps.Features.Localization;
using Beeps.Features.RateLimiting;
using Beeps.Features.Shared.Http;
using Beeps.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages(options => options.Conventions.AddBeepsAuthorization());
builder.Services.AddBeepsApiJson();
builder.Services.AddBeepsProblemDetails();
builder.Services.AddBeepsLocalization();
builder.Services.AddBeepsRateLimiting();
builder.Services.AddBeepsAuth();
builder.Services.AddBeepsFeatures();
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

// Authentication first: the saved language and the per-account rate limit both need the user.
app.UseAuthentication();
app.UseRequestLocalization();
app.UseRouting();
app.UseMiddleware<RateLimitingMiddleware>();
app.UseAuthorization();

app.MapStaticAssets().WithoutRateLimit();
app.MapRazorPages().WithStaticAssets();
app.MapAvatarEndpoints();
app.MapAgentApi();

app.Run();

/// <summary>Entry point; public so the integration tests can host the app.</summary>
public partial class Program;
