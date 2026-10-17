using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Beeps.Features.RateLimiting;
using Beeps.Features.Shared.Http;
using Beeps.Tests.Support;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Beeps.Tests.Api;

/// <summary>Rules every agent endpoint must follow (PLAN 12), checked over all of them so a new one cannot slip by.</summary>
public sealed partial class ApiContractTests : IClassFixture<BeepsWebApplicationFactory>
{
    private readonly BeepsWebApplicationFactory factory;

    public ApiContractTests(BeepsWebApplicationFactory factory)
    {
        this.factory = factory;
        // Starts the host, so the endpoints are built.
        factory.CreateClient().Dispose();
    }

    [Fact]
    public void EveryEndpointExplainsItselfToAgents() =>
        Assert.All(Endpoints(), e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Endpoint.Metadata.GetMetadata<IEndpointSummaryMetadata>()?.Summary), e.Name);
            Assert.False(string.IsNullOrWhiteSpace(e.Endpoint.Metadata.GetMetadata<IEndpointDescriptionMetadata>()?.Description), e.Name);
        });

    [Fact]
    public void EveryEndpointHasARateLimit() =>
        Assert.All(Endpoints(), e => Assert.NotNull(e.Endpoint.Metadata.GetMetadata<RateLimitPolicyAttribute>()));

    [Fact]
    public void EveryWriteAcceptsAnIdempotencyKey() =>
        Assert.All(Endpoints().Where(e => e.Method != "GET"), e => Assert.NotNull(e.Endpoint.Metadata.GetMetadata<IdempotentAttribute>()));

    [Fact]
    public async Task SkillMdDocumentsEveryEndpoint()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(new Uri("/skill.md", UriKind.Relative), TestSupport.Ct);
        var skill = await response.Content.ReadAsStringAsync(TestSupport.Ct);

        Assert.Equal("text/markdown", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("https://beeps.test/api/v1/agent/hello", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("{{BASE_URL}}", skill, StringComparison.Ordinal);
        Assert.All(Endpoints(), e => Assert.Contains($"`{e.Name}`", skill, StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheOpenApiDocumentListsEveryEndpoint()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(new Uri(OpenApiSetup.DocumentPath, UriKind.Relative), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestSupport.Ct));
        var paths = document.RootElement.GetProperty("paths");
        Assert.All(Endpoints(), e =>
        {
            var path = e.Name[(e.Method.Length + 1)..];
            Assert.True(paths.TryGetProperty(path, out var operations), path);
            Assert.True(operations.TryGetProperty(e.Method.ToLowerInvariant(), out _), e.Name);
        });
        Assert.True(document.RootElement.GetProperty("components").GetProperty("securitySchemes").TryGetProperty("agent_key", out _));
    }

    /// <summary>The agent API, as "METHOD /path" with route constraints removed (<c>{id:guid}</c> → <c>{id}</c>).</summary>
    private List<(string Name, string Method, RouteEndpoint Endpoint)> Endpoints()
    {
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/v1/", StringComparison.Ordinal) == true
                && e.RoutePattern.RawText != OpenApiSetup.DocumentPath)
            .SelectMany(e => e.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods
                .Select(m => ($"{m} {Constraint().Replace(e.RoutePattern.RawText!, "{$1}").TrimEnd('/')}", m, e)))
            .ToList();
        Assert.NotEmpty(endpoints);
        return endpoints;
    }

    [GeneratedRegex(@"\{(\w+):[^}]+\}")]
    private static partial Regex Constraint();
}
