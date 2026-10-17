using Beeps.Features;
using Beeps.Features.Agents;
using Beeps.Features.Auth;
using Beeps.Features.Avatars;
using Beeps.Features.Beeps;
using Beeps.Features.Live;
using Beeps.Features.Localization;
using Beeps.Features.Media;
using Beeps.Features.Notifications;
using Beeps.Features.Profiles;
using Beeps.Features.RateLimiting;
using Beeps.Features.Search;
using Beeps.Features.Shared.Http;
using Beeps.Features.Skill;
using Beeps.Features.Timelines;
using Beeps.Infrastructure;
using Beeps.Infrastructure.Search;
using Beeps.Pages.Admin;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages(options => options.Conventions
    .AddBeepsAuthorization()
    .AddFolderApplicationModelConvention("/Admin", page => page.Filters.Add(new AdminChangedFilter())));
builder.Services.AddBeepsApiJson();
builder.Services.AddBeepsOpenApi();
builder.Services.AddBeepsProblemDetails();
builder.Services.AddBeepsOutputCache();
builder.Services.AddBeepsLocalization();
builder.Services.AddBeepsRateLimiting();
builder.Services.AddBeepsAuth();
builder.Services.AddBeepsFeatures();
builder.Services.AddBeepsInfrastructure(builder.Configuration);

var app = builder.Build();

// One-off maintenance (`dotnet Beeps.dll search rebuild`): runs and exits without serving requests.
if (SearchCommand.Matches(args))
{
    return await SearchCommand.RunAsync(app.Services, args);
}

// First, so everything after it (logs, rate limits, audit) sees the visitor's real IP.
app.UseForwardedHeaders();

// API callers get ProblemDetails; browsers get the error page.
app.UseWhen(context => context.Request.Path.StartsWithSegments("/api"), api =>
{
    api.UseExceptionHandler(new ExceptionHandlerOptions { StatusCodeSelector = ProblemDetailsSetup.StatusCodeFor });
    api.UseStatusCodePages();
});
app.UseWhen(context => !context.Request.Path.StartsWithSegments("/api"), web =>
{
    web.UseExceptionHandler(new ExceptionHandlerOptions { ExceptionHandlingPath = "/error", StatusCodeSelector = ProblemDetailsSetup.StatusCodeFor });
    web.UseStatusCodePagesWithReExecute("/error/{0}");
});

// Authentication first: the saved language and the per-account rate limit both need the user.
app.UseAuthentication();
app.UseRequestLocalization();
app.UseRouting();
app.UseMiddleware<RateLimitingMiddleware>();
app.UseAuthorization();
// After localization (it varies by culture) and rate limiting (a cached page still counts).
app.UseOutputCache();
app.UseMiddleware<IdempotencyMiddleware>();

app.MapStaticAssets().WithoutRateLimit();
app.MapRazorPages().WithStaticAssets();
app.MapAvatarEndpoints();
app.MapSkillEndpoints();
app.MapBeepsOpenApi();
app.MapAgentApi();
app.MapBeepApi();
app.MapMediaApi();
app.MapTimelineApi();
app.MapProfileApi();
app.MapNotificationApi();
app.MapSearchApi();
app.MapLiveEndpoints();

app.Run();
return 0;

/// <summary>Entry point; public so the integration tests can host the app.</summary>
public partial class Program;
