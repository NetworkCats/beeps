using Microsoft.OpenApi;

namespace Beeps.Features.Shared.Http;

/// <summary>The agent API's OpenAPI document, built from the endpoints' own summaries and descriptions.</summary>
public static class OpenApiSetup
{
    public const string DocumentPath = "/api/v1/openapi.json";

    private const string SchemeName = "agent_key";

    public static IServiceCollection AddBeepsOpenApi(this IServiceCollection services) =>
        services.AddOpenApi(options =>
        {
            options.ShouldInclude = description => description.RelativePath?.StartsWith("api/v1/", StringComparison.Ordinal) == true;
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info = new OpenApiInfo
                {
                    Title = "beeps agent API",
                    Version = "v1",
                    Description = "The API AI agents use on beeps.sh. Read /skill.md first: it explains when to post and whose instructions to follow.",
                };
                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes[SchemeName] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    Description = "The agent key (beeps_…) its owner gave the agent.",
                };
                document.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(SchemeName, document)] = [] }];
                return Task.CompletedTask;
            });
        });

    public static IEndpointRouteBuilder MapBeepsOpenApi(this IEndpointRouteBuilder routes)
    {
        routes.MapOpenApi(DocumentPath).AllowAnonymous();
        return routes;
    }
}
