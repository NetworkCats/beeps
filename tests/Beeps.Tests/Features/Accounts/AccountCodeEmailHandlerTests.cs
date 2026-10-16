using System.Text.Json;
using Beeps.Features.Accounts;
using Beeps.Features.Email;
using Beeps.Features.Shared;
using Beeps.Tests.Support;
using Microsoft.AspNetCore.DataProtection;

namespace Beeps.Tests.Features.Accounts;

public sealed class AccountCodeEmailHandlerTests
{
    private readonly IEmailSender sender = Substitute.For<IEmailSender>();
    private readonly EphemeralDataProtectionProvider dataProtection = new();

    [Fact]
    public async Task SendsTheDecryptedCodeWithTheMessageIdAsIdempotencyKey()
    {
        var handler = CreateHandler();
        var payload = new AccountCodeEmail("tom@example.com", "en", EmailCodePurpose.VerifyEmail, Protect("123456"));
        var message = new OutboxEnvelope(Guid.NewGuid(), handler.Type, JsonSerializer.Serialize(payload, AccountsJsonContext.Default.AccountCodeEmail), 1, TestData.Now);

        await handler.HandleAsync(message, TestSupport.Ct);

        await sender.Received(1).SendAsync(
            Arg.Is<EmailMessage>(m => m.To == "tom@example.com" && m.Subject.Contains("123456", StringComparison.Ordinal) && m.TextBody.Contains("123456", StringComparison.Ordinal)),
            message.Id.ToString("D"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ThePayloadNeverHoldsTheCodeInTheClear()
    {
        var json = JsonSerializer.Serialize(
            new AccountCodeEmail("tom@example.com", "en", EmailCodePurpose.ResetPassword, Protect("654321")),
            AccountsJsonContext.Default.AccountCodeEmail);

        Assert.DoesNotContain("654321", json, StringComparison.Ordinal);
        Assert.Contains("\"purpose\":\"ResetPassword\"", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en", EmailCodePurpose.VerifyEmail, "is your beeps verification code", "expires in 10 minutes")]
    [InlineData("zh-Hans", EmailCodePurpose.VerifyEmail, "beeps 验证码", "10 分钟内有效")]
    [InlineData("en", EmailCodePurpose.ResetPassword, "password reset code", "expires in 10 minutes")]
    [InlineData("zh-Hans", EmailCodePurpose.ResetPassword, "重置密码验证码", "10 分钟内有效")]
    public void IsWrittenInTheRecipientsLanguage(string locale, EmailCodePurpose purpose, string subject, string expiry)
    {
        var email = CreateHandler().Compose(new AccountCodeEmail("tom@example.com", locale, purpose, ""), "123456");

        Assert.Contains(subject, email.Subject, StringComparison.Ordinal);
        Assert.Contains(expiry, email.TextBody, StringComparison.Ordinal);
        Assert.Contains("<strong>123456</strong>", email.HtmlBody, StringComparison.Ordinal);
    }

    private string Protect(string code) => dataProtection.CreateProtector(AccountCodeEmail.ProtectorPurpose).Protect(code);

    private AccountCodeEmailHandler CreateHandler() => new(sender, dataProtection, RealLocalizer.Create());
}
