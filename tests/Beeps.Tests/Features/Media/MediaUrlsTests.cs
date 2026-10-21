using Beeps.Tests.Support;

namespace Beeps.Tests.Features.Media;

public sealed class MediaUrlsTests
{
    [Fact]
    public void AMediaUrlNamesItsObject() =>
        Assert.Equal("face/avatar.webp", TestData.Media.ObjectName(TestData.Media.Avatar("face")));

    [Theory]
    [InlineData("https://beeps.test/avatars/3f2a8c1e-5b7d-4e9a-8c6f-1d2e3f4a5b6c.svg")]
    [InlineData("https://media.beeps.test.evil.example/face/avatar.webp")]
    [InlineData("http://media.beeps.test/face/avatar.webp")]
    public void AnAddressElsewhereNamesNoObject(string url) =>
        Assert.Null(TestData.Media.ObjectName(new Uri(url)));
}
