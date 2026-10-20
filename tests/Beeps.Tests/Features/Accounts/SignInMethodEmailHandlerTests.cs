using Beeps.Features.Accounts;
using Beeps.Features.Email;
using Beeps.Tests.Support;

namespace Beeps.Tests.Features.Accounts;

public sealed class SignInMethodEmailHandlerTests
{
    private readonly IEmailSender sender = Substitute.For<IEmailSender>();

    [Fact]
    public async Task SendsWithTheMessageIdAsIdempotencyKey()
    {
        var handler = CreateHandler();
        var message = TestData.Envelope(handler.Type, new SignInMethodEmail("tom@example.com", "en", "Google"), AccountsJsonContext.Default.SignInMethodEmail);

        await handler.HandleAsync(message, TestSupport.Ct);

        await sender.Received(1).SendAsync(
            Arg.Is<EmailMessage>(m => m.To == "tom@example.com" && m.TextBody.Contains("Google", StringComparison.Ordinal)),
            message.Id.ToString("D"),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("en", null, "a passkey.", "reset your password")]
    [InlineData("en", "GitHub", "GitHub.", "reset your password")]
    [InlineData("zh-Hans", null, "通行密钥。", "请重置密码")]
    [InlineData("zh-Hans", "Google", "Google。", "请重置密码")]
    public void IsWrittenInTheRecipientsLanguage(string locale, string? provider, string method, string advice)
    {
        var email = CreateHandler().Compose(new SignInMethodEmail("tom@example.com", locale, provider));

        Assert.Contains(method, email.TextBody, StringComparison.Ordinal);
        Assert.Contains(advice, email.TextBody, StringComparison.Ordinal);
        Assert.Contains(advice, email.HtmlBody, StringComparison.Ordinal);
    }

    private SignInMethodEmailHandler CreateHandler() => new(sender, RealLocalizer.Create());
}
