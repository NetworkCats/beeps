using Microsoft.Extensions.Localization;

namespace Beeps.Tests.Support;

/// <summary>Returns the resource key (plus any arguments) as the text, so tests can assert which message was chosen.</summary>
internal sealed class TestLocalizer : IStringLocalizer<SharedResource>
{
    public LocalizedString this[string name] => new(name, name);

    public LocalizedString this[string name, params object[] arguments] => new(name, $"{name}({string.Join(",", arguments)})");

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
}
