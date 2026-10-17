using Beeps.Features.Auth;
using Beeps.Features.Live;
using Beeps.Features.RateLimiting;
using Beeps.Features.Search;
using Beeps.Features.Site;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Beeps.Pages;

/// <summary>Searching beeps and people (PLAN 6.18). <c>#tag</c> goes straight to the hashtag's page (PLAN 6.19).</summary>
[RateLimitPolicy(RateLimitPolicies.Search)]
public sealed class SearchModel(SearchService search) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "q")] public string? Query { get; set; }

    /// <summary><see cref="SearchTypes.Beeps"/> or <see cref="SearchTypes.Users"/>.</summary>
    [BindProperty(SupportsGet = true)] public string? Type { get; set; }

    [BindProperty(SupportsGet = true)] public int? Offset { get; set; }

    /// <summary>Null until something was searched.</summary>
    public SearchResults? Results { get; private set; }

    /// <summary>Results are asked again as the index changes; there is nothing to follow before searching.</summary>
    public LiveTopic Live => Results is null ? LiveTopic.None : LiveTopic.Search;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (SearchService.HashtagShortcut(Query) is { } tag)
        {
            return LocalRedirect(SitePaths.Tag(tag));
        }

        if (string.IsNullOrWhiteSpace(Query))
        {
            return Page();
        }

        var result = await search.SearchAsync(User.OptionalViewer(), new SearchRequest(Query, Type, Offset: Offset), cancellationToken);
        Results = result.IsSuccess ? result.Value : null;
        return result.IsSuccess ? Page() : this.ShowError(result.Error);
    }
}
