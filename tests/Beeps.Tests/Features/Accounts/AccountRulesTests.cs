using Beeps.Features.Accounts;

namespace Beeps.Tests.Features.Accounts;

public sealed class AccountRulesTests
{
    [Theory]
    [InlineData("tom", true)]
    [InlineData("Tom_99", true)]
    [InlineData("abcdefghij0123456789", true)]
    [InlineData("ab", false)]
    [InlineData("abcdefghij0123456789x", false)]
    [InlineData("tom.claude", false)]
    [InlineData("tom-c", false)]
    [InlineData("汤姆", false)]
    [InlineData(" tom", false)]
    [InlineData("tom\n", false)]
    [InlineData(null, false)]
    public void Usernames(string? username, bool valid) => Assert.Equal(valid, AccountRules.IsValidUsername(username));

    [Fact]
    public void ReservedNamesIgnoreCase()
    {
        Assert.True(AccountRules.IsReservedUsername("Admin"));
        Assert.False(AccountRules.IsReservedUsername("tom"));
    }

    [Theory]
    [InlineData("compose")]
    [InlineData("Public")]
    [InlineData("notifications")]
    [InlineData("culture")]
    [InlineData("error")]
    public void SingleSegmentPagesAreReserved(string name) => Assert.True(AccountRules.IsReservedUsername(name));

    [Theory]
    [InlineData("tom", true)]
    [InlineData("tom.claude2", true)]
    [InlineData("Tom_1.My_Agent", true)]
    [InlineData("tom.", false)]
    [InlineData(".claude", false)]
    [InlineData("a.b.c", false)]
    [InlineData("tom.claude\n", false)]
    [InlineData("wp-login.php", false)]
    [InlineData("汤姆", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void HandleShapes(string? handle, bool plausible) => Assert.Equal(plausible, AccountRules.IsPlausibleHandle(handle));

    [Fact]
    public void HandlesLongerThanTheColumnAreNotPlausible() =>
        Assert.False(AccountRules.IsPlausibleHandle(new string('a', 65)));

    [Theory]
    [InlineData("tom@example.com", true)]
    [InlineData("Tom.Smith+beeps@mail.example.co", true)]
    [InlineData("tom@localhost", false)]
    [InlineData("Tom <tom@example.com>", false)]
    [InlineData("not-an-email", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Emails(string? email, bool valid) => Assert.Equal(valid, AccountRules.IsValidEmail(email));

    [Fact]
    public void EmailsAndHandlesNormalizeToLowerCase()
    {
        Assert.Equal("tom@example.com", AccountRules.NormalizeEmail(" Tom@Example.COM "));
        Assert.Equal("tom_1", AccountRules.NormalizeHandle("Tom_1"));
    }

    [Theory]
    [InlineData("1234567", false)]
    [InlineData("12345678", true)]
    [InlineData("十二个汉字的中文密码短语也可", true)]
    [InlineData(null, false)]
    public void Passwords(string? password, bool valid) => Assert.Equal(valid, AccountRules.IsValidPassword(password));

    [Theory]
    [InlineData("012345", true)]
    [InlineData("12345", false)]
    [InlineData("12345a", false)]
    [InlineData("١٢٣٤٥٦", false)]
    public void Codes(string code, bool valid) => Assert.Equal(valid, AccountRules.IsWellFormedCode(code));

    [Fact]
    public void InviteCodesForgiveCaseSpacesAndDashes()
    {
        var code = InviteCodes.Generate();

        Assert.Matches("^[0-9A-Z]{4}-[0-9A-Z]{4}-[0-9A-Z]{4}-[0-9A-Z]{4}$", code);
        Assert.DoesNotMatch("[ILOU]", code);
        Assert.Equal(InviteCodes.Hash(code), InviteCodes.Hash(code.ToLowerInvariant().Replace('-', ' ')));
        Assert.Equal(code[..4], InviteCodes.Prefix(code));
        Assert.NotEqual(code, InviteCodes.Generate());
    }
}
