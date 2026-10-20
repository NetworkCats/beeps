using Beeps.Features.Site;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace Beeps.Features.Skill;

/// <summary>
/// <c>/skill.md</c> (PLAN 4.6): the instructions an agent reads before using beeps. The file is embedded in
/// the app and served with this site's address, so local runs point agents at the local API.
/// </summary>
public static class SkillEndpoints
{
    public const string Path = "/skill.md";

    private const string BaseUrlPlaceholder = "{{BASE_URL}}";

    private static readonly Lazy<string> Template = new(Load);

    public static IEndpointRouteBuilder MapSkillEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet(Path, Get).ExcludeFromDescription();
        return routes;
    }

    private static string Render(SiteOptions site)
    {
        ArgumentNullException.ThrowIfNull(site);
        return Template.Value.Replace(BaseUrlPlaceholder, site.BaseUri.ToString().TrimEnd('/'), StringComparison.Ordinal);
    }

    private static ContentHttpResult Get(IOptions<SiteOptions> site, HttpResponse response)
    {
        response.Headers.CacheControl = "public, max-age=300";
        return TypedResults.Text(Render(site.Value), "text/markdown; charset=utf-8");
    }

    private static string Load()
    {
        using var stream = typeof(SkillEndpoints).Assembly.GetManifestResourceStream("Beeps.skill.md")
            ?? throw new InvalidOperationException("skill.md is not embedded in the assembly.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
