using System.Security.Claims;
using Beeps.Features.Auth;
using Beeps.Features.Beeps;
using Beeps.Features.Live;
using Beeps.Features.Notifications;
using Beeps.Features.Shared;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

namespace Beeps.Pages.Islands;

/// <summary>
/// What the islands fetch as they follow the site live (PLAN 6.10), as the props they are drawn from (JSON), for
/// members only. A page's own props come from the page itself (<see cref="IslandRequests"/>); these are the parts a
/// live event asks for: the beeps after the ones a list shows, beeps drawn again, the inbox's first page, the viewer.
/// Besides, what a page tells the site: that a beep was viewed.
/// </summary>
public static class IslandEndpoints
{
    private const string Json = "application/json; charset=utf-8";

    public static IEndpointRouteBuilder MapIslandEndpoints(this IEndpointRouteBuilder routes)
    {
        // Under /live: refused outright when signed out, never a login redirect (AuthSetup.IsLive).
        var live = routes.MapGroup("/live").RequireAuthorization(AuthPolicies.Member).ExcludeFromDescription();
        live.MapGet("/beeps", GetBeepsAsync);
        live.MapGet("/notifications", GetNotificationsAsync);
        live.MapGet("/viewer", (ClaimsPrincipal user, ClientConfig config) => Results.Content(IslandJson.Serialize(config.Viewer(user)), Json));
        // Anyone signed in, verified or not: a beep's page says it was viewed.
        routes.MapPost("/live/beeps/{id:guid}/view", PostViewAsync).RequireAuthorization(AuthPolicies.SignedIn).ExcludeFromDescription();
        return routes;
    }

    /// <summary>The beep's page was shown (<see cref="BeepService.RecordViewAsync"/>); a form with the antiforgery token, answered with a status code only.</summary>
    private static async Task<IResult> PostViewAsync(Guid id, HttpContext context, IAntiforgery antiforgery, BeepService beeps, CancellationToken cancellationToken)
    {
        if (!await antiforgery.IsRequestValidAsync(context))
        {
            return Results.BadRequest();
        }

        var result = await beeps.RecordViewAsync(context.User.Viewer(), id, cancellationToken);
        return result.IsSuccess ? Results.NoContent() : Results.NotFound();
    }

    /// <summary>
    /// Either the beeps after the one a page shows last (the stream announced new ones, or the page catches up), or,
    /// with <paramref name="ids"/>, some it shows, again: the ones the viewer may no longer see are left out.
    /// </summary>
    /// <param name="watch">The page's <see cref="LiveTopic"/>, for its new beeps.</param>
    /// <param name="after">The newest beep a list shows, or the last one of a thread; the empty id when the page shows none.</param>
    /// <param name="before">For a list: only beeps older than this one (the rest of a burst too big for one answer).</param>
    /// <param name="ids">Comma-separated: the beeps to draw again, instead of the new ones.</param>
    private static async Task<IResult> GetBeepsAsync(
        [FromQuery] string? watch,
        [FromQuery] Guid? after,
        [FromQuery] Guid? before,
        [FromQuery] string? ids,
        ClaimsPrincipal user,
        LiveFeedService live,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<BeepView>> result;
        if (ids is null)
        {
            result = await live.GetNewBeepsAsync(user.Viewer(), watch, after ?? Guid.Empty, before, cancellationToken);
        }
        else if (TryParseIds(ids, out var parsed))
        {
            result = await live.GetBeepsAsync(user.Viewer(), parsed, cancellationToken);
        }
        else
        {
            return Results.BadRequest();
        }

        return result.IsSuccess
            ? Results.Content(IslandJson.Serialize(new LiveItems<BeepProps>([.. result.Value.Select(BeepProps.From)])), Json)
            : Results.BadRequest();
    }

    /// <summary>
    /// The entries newer than the newest an inbox page shows, or, without <paramref name="after"/>, the newest
    /// <paramref name="limit"/> entries, to bring the whole page up to date (read in another tab, gone with their beep).
    /// </summary>
    private static async Task<IResult> GetNotificationsAsync(
        [FromQuery] Guid? after,
        [FromQuery] int? limit,
        ClaimsPrincipal user,
        NotificationService notifications,
        CancellationToken cancellationToken)
    {
        var page = await notifications.ListAsync(user.Viewer(), PageRequest.Create(sinceId: after, limit: limit ?? LiveFeedService.Limit), unreadOnly: false, cancellationToken);
        return Results.Content(IslandJson.Serialize(new LiveItems<NotificationProps>([.. page.Items.Select(NotificationProps.From)])), Json);
    }

    private static bool TryParseIds(string value, out List<Guid> ids)
    {
        ids = [];
        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Guid.TryParse(part, out var id))
            {
                return false;
            }

            ids.Add(id);
        }

        return true;
    }
}
