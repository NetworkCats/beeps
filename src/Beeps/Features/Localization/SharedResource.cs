// Deliberately in the root namespace and outside Resources/: the localizer then resolves
// Resources/SharedResource*.resx, and the SDK does not rename those resources after this class.
namespace Beeps;

/// <summary>Marker type for the site-wide UI strings in <c>Resources/SharedResource*.resx</c>.</summary>
public sealed class SharedResource;
