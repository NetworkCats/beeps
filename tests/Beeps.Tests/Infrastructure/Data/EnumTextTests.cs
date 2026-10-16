using Beeps.Features.Accounts;
using Beeps.Infrastructure.Data;

namespace Beeps.Tests.Infrastructure.Data;

public sealed class EnumTextTests
{
    [Theory]
    [InlineData(RegistrationMode.Open, "open")]
    [InlineData(RegistrationMode.InviteOnly, "invite_only")]
    [InlineData(RegistrationMode.Closed, "closed")]
    public void RoundTripsAsSnakeCase(RegistrationMode mode, string text)
    {
        Assert.Equal(text, EnumText.ToValue(mode));
        Assert.Equal(mode, EnumText.Parse<RegistrationMode>(text));
    }

    [Fact]
    public void UnknownTextIsAnError() =>
        Assert.Throws<InvalidOperationException>(() => EnumText.Parse<ActorKind>("robot"));
}
