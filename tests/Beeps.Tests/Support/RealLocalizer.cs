using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Beeps.Tests.Support;

internal static class RealLocalizer
{
    /// <summary>The app's actual .resx strings, for tests about wording and languages.</summary>
    public static IStringLocalizer<SharedResource> Create() =>
        new StringLocalizer<SharedResource>(new ResourceManagerStringLocalizerFactory(
            Options.Create(new LocalizationOptions { ResourcesPath = "Resources" }),
            NullLoggerFactory.Instance));
}
