using Beeps.Features;
using Beeps.Features.Agents;
using Beeps.Features.Auth;
using Beeps.Features.Avatars;
using Beeps.Features.Beeps;
using Beeps.Features.Crawlers;
using Beeps.Features.Live;
using Beeps.Features.Localization;
using Beeps.Features.Media;
using Beeps.Features.Notifications;
using Beeps.Features.Profiles;
using Beeps.Features.RateLimiting;
using Beeps.Features.Search;
using Beeps.Features.Shared.Http;
using Beeps.Features.Site;
using Beeps.Features.Skill;
using Beeps.Features.Timelines;
using Beeps.Infrastructure;
using Beeps.Infrastructure.Data;
using Beeps.Infrastructure.ErrorReporting;
using Beeps.Infrastructure.Http;
using Beeps.Infrastructure.Search;
using Beeps.Pages;
using Beeps.Pages.Admin;
using Beeps.Pages.Islands;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseBeepsErrorReporting(builder.Configuration);

builder.Services.AddRazorPages(options => options.Conventions
    .AddBeepsAuthorization()
    .ConfigureFilter(new NoHandlerFilter())
    .AddFolderApplicationModelConvention("/Admin", page =>
    {
        page.Filters.Add(new AdminChangedFilter());
        page.Filters.Add(new AppShellAttribute());
    }));
// Named after beeps, not the framework (CookieNames).
builder.Services.AddAntiforgery(options => options.Cookie.Name = CookieNames.Antiforgery);
builder.Services.Configure<CookieTempDataProviderOptions>(options => options.Cookie.Name = CookieNames.Notice);
builder.Services.AddScoped<ClientConfig>();
builder.Services.AddSingleton<ClientStrings>();
builder.Services.AddSingleton<ClientChunks>();
builder.Services.AddBeepsApiJson();
builder.Services.AddBeepsOpenApi();
builder.Services.AddBeepsProblemDetails();
builder.Services.AddBeepsOutputCache();
builder.Services.AddBeepsLocalization();
builder.Services.AddBeepsRateLimiting();
builder.Services.AddBeepsAuth(builder.Configuration);
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
// Outside everything that answers, error pages included.
app.UseMiddleware<SecurityHeadersMiddleware>();
// A page's data is asked for as the page itself, before the error pages, so a page not found answers as JSON too.
app.UseMiddleware<PageDataMiddleware>();

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

// The admin pages' scripts and stylesheet for admins only; the other chunks kept for good.
app.UseMiddleware<ClientAssetsMiddleware>();

app.MapStaticAssets().WithoutRateLimit();
// For Docker and the blue-green deploy, from the host only: nginx does not pass it on.
app.MapHealthChecks(DatabaseHealthCheck.Path).WithoutRateLimit();
app.MapRazorPages().WithStaticAssets();
app.MapAvatarEndpoints();
app.MapSkillEndpoints();
app.MapCrawlerEndpoints();
app.MapBeepsOpenApi();
app.MapAgentApi();
app.MapBeepApi();
app.MapMediaApi();
app.MapTimelineApi();
app.MapProfileApi();
app.MapNotificationApi();
app.MapSearchApi();
app.MapLiveEndpoints();
app.MapIslandEndpoints();
app.MapClientStrings();

app.Run();
return 0;

/// <summary>Entry point; public so the integration tests can host the app.</summary>
public partial class Program;
