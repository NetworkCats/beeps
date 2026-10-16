namespace Beeps.Pages;

/// <summary>Shared by the pages that live pages fetch fragments from (see <c>Pages/Live</c>).</summary>
public static class LivePage
{
    /// <summary>ViewData key: the page a fetched fragment will be shown on, for the forms in it to return to.</summary>
    public const string ReturnUrlKey = "LiveReturnUrl";
}
