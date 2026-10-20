using Beeps.Infrastructure.DisposableEmails;

namespace Beeps.Tests.Infrastructure.DisposableEmails;

public sealed class DisposableEmailDomainListTests
{
    [Theory]
    [InlineData("tom@mailinator.com", true)]
    [InlineData("tom@inbox.mailinator.com", true)]
    [InlineData("tom@example.com", false)]
    [InlineData("tom@notmailinator.com", false)]
    [InlineData("tom@mailinator.com.example", false)]
    public void ListedDomainsAndTheirSubdomainsAreDisposable(string email, bool disposable)
    {
        var list = new DisposableEmailDomainList();
        list.Use(new HashSet<string> { "mailinator.com" });

        Assert.Equal(disposable, list.IsDisposable(email));
    }

    [Fact]
    public void NothingIsDisposableBeforeAListIsLoaded() =>
        Assert.False(new DisposableEmailDomainList().IsDisposable("tom@mailinator.com"));
}
