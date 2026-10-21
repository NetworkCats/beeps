using Beeps.Features.Site;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace Beeps.Features.Skill;

/// <summary>
/// <c>/skill.md</c>: the instructions an agent reads before using beeps, and <c>/skill/reference.md</c>: the API details
/// it reads when it needs them. The files are embedded in the app and served with this site's address, so local runs
/// point agents at the local API.
/// </summary>
public static class SkillEndpoints
{
    public const string Path = "/skill.md";

    public const string ReferencePath = "/skill/reference.md";

    private const string BaseUrlPlaceholder = "{{BASE_URL}}";

    private static readonly Lazy<string> Skill = new(() => Load("Beeps.skill.md"));

    private static readonly Lazy<string> Reference = new(() => Load("Beeps.skill-reference.md"));

    public static IEndpointRouteBuilder MapSkillEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet(Path, (IOptions<SiteOptions> site, HttpResponse response) => Get(Skill, site.Value, response))
            .ExcludeFromDescription();
        routes.MapGet(ReferencePath, (IOptions<SiteOptions> site, HttpResponse response) => Get(Reference, site.Value, response))
            .ExcludeFromDescription();
        return routes;
    }

    private static string Render(string template, SiteOptions site)
    {
        ArgumentNullException.ThrowIfNull(site);
        return template.Replace(BaseUrlPlaceholder, site.BaseUri.ToString().TrimEnd('/'), StringComparison.Ordinal);
    }

    private static ContentHttpResult Get(Lazy<string> template, SiteOptions site, HttpResponse response)
    {
        response.Headers.CacheControl = "public, max-age=300";
        return TypedResults.Text(Render(template.Value, site), "text/markdown; charset=utf-8");
    }

    private static string Load(string name)
    {
        using var stream = typeof(SkillEndpoints).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"{name} is not embedded in the assembly.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
