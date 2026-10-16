using System.Globalization;
using Beeps.Features.Media;
using Beeps.Features.Shared;
using Beeps.Features.Shared.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Beeps.Pages;

public static class PageModelExtensions
{
    /// <summary>What Services need to know about the request; the IP is the one ForwardedHeaders resolved.</summary>
    public static RequestOrigin Origin(this PageModel page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return new RequestOrigin(
            page.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            CultureInfo.CurrentUICulture.Name);
    }

    /// <summary>
    /// Uploaded files for the media Services. A file input left empty still sends a nameless, empty part;
    /// it is no image, so it is left out.
    /// </summary>
    public static IReadOnlyList<MediaContent> ToMediaContents(this IEnumerable<IFormFile>? files) =>
        files is null ? [] : [.. files.Where(f => f.Length > 0).Select(f => new MediaContent(f.OpenReadStream(), f.Length))];

    /// <summary>One uploaded file; a form sent without one gives an empty upload, which the Service rejects.</summary>
    public static MediaContent ToMediaContent(this IFormFile? file) =>
        file is { Length: > 0 } ? new MediaContent(file.OpenReadStream(), file.Length) : new MediaContent(Stream.Null, 0);

    /// <summary>A sign-in or sign-up form posted by someone already signed in: they stay who they are, and are told so.</summary>
    public static LocalRedirectResult AlreadySignedIn(this PageModel page, IStringLocalizer<SharedResource> localizer)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(localizer);
        page.TempData[Notices.Error] = localizer["Error.AlreadySignedIn", page.User.Identity?.Name ?? ""].Value;
        return page.LocalRedirect("~/");
    }

    /// <summary>
    /// Shows a Service error on the re-rendered form: field errors next to their inputs, anything else at
    /// the top. The status code follows the error, so a rate-limited form answers 429 with Retry-After.
    /// </summary>
    public static PageResult ShowError(this PageModel page, Error error)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(error);
        if (error.Fields is { Count: > 0 } fields)
        {
            foreach (var (field, messages) in fields)
            {
                foreach (var message in messages)
                {
                    page.ModelState.AddModelError(field, message);
                }
            }
        }
        else
        {
            page.ModelState.AddModelError(string.Empty, error.Detail ?? error.Code);
        }

        if (error.RetryAfter is { } retryAfter)
        {
            page.Response.Headers.RetryAfter = ErrorHttpResult.RetryAfterHeaderValue(retryAfter);
        }

        var result = page.Page();
        result.StatusCode = ErrorHttpResult.StatusCodeFor(error.Kind);
        return result;
    }
}
