using Beeps.Features.Accounts;
using Beeps.Features.Shared;
using Beeps.Features.Site;
using Microsoft.Extensions.Options;

namespace Beeps.Tests.Support;

internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 10, 16, 8, 30, 0, TimeSpan.Zero);

    public static readonly RequestOrigin Origin = new("203.0.113.7", "en");

    public static IOptions<SiteOptions> Site { get; } = Options.Create(new SiteOptions { BaseUrl = "https://beeps.test" });

    public static TimeProvider Time()
    {
        var time = Substitute.For<TimeProvider>();
        time.GetUtcNow().Returns(Now);
        return time;
    }

    public static User User(string handle = "tom", bool admin = false, bool verified = true, string? passwordHash = "hash")
    {
        var id = Guid.CreateVersion7(Now);
        return new User
        {
            Id = id,
            Actor = new Actor
            {
                Id = id,
                Kind = ActorKind.Human,
                Handle = handle,
                NormalizedHandle = handle.ToLowerInvariant(),
                DisplayName = handle,
                CreatedAt = Now,
            },
            Email = $"{handle}@example.com",
            NormalizedEmail = $"{handle.ToLowerInvariant()}@example.com",
            PasswordHash = passwordHash ?? "",
            SecurityStamp = "stamp-1",
            EmailVerifiedAt = verified ? Now : null,
            IsAdmin = admin,
            Locale = "en",
            CreatedAt = Now,
        };
    }
}
