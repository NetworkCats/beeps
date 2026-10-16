using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Beeps.Tests.Support;

/// <summary>Returns the resource key (plus any arguments) as the text, so tests can assert which message was chosen.</summary>
internal sealed class TestLocalizer : IStringLocalizer<SharedResource>
{
    public LocalizedString this[string name] => new(name, name);

    public LocalizedString this[string name, params object[] arguments] => new(name, $"{name}({string.Join(",", arguments)})");

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
}

internal static class RealLocalizer
{
    /// <summary>The app's actual .resx strings, for tests about wording and languages.</summary>
    public static IStringLocalizer<SharedResource> Create() =>
        new StringLocalizer<SharedResource>(new ResourceManagerStringLocalizerFactory(
            Options.Create(new LocalizationOptions { ResourcesPath = "Resources" }),
            NullLoggerFactory.Instance));
}
