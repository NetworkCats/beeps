using Beeps.Features.Accounts;

namespace Beeps.Tests.Features.Accounts;

public sealed class UserAgentsTests
{
    [Theory]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1", DeviceKind.Phone, "iPhone", "Safari")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) CriOS/130.0.6723.90 Mobile/15E148 Safari/604.1", DeviceKind.Phone, "iPhone", "Chrome")]
    [InlineData("Mozilla/5.0 (iPad; CPU OS 17_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Mobile/15E148 Safari/604.1", DeviceKind.Tablet, "iPad", "Safari")]
    [InlineData("Mozilla/5.0 (Linux; Android 10; K) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Mobile Safari/537.36", DeviceKind.Phone, "Android", "Chrome")]
    [InlineData("Mozilla/5.0 (Linux; Android 14; SM-X710) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36", DeviceKind.Tablet, "Android", "Chrome")]
    [InlineData("Mozilla/5.0 (Linux; Android 14; SM-S918B) AppleWebKit/537.36 (KHTML, like Gecko) SamsungBrowser/26.0 Chrome/122.0.0.0 Mobile Safari/537.36", DeviceKind.Phone, "Android", "Samsung Internet")]
    [InlineData("Mozilla/5.0 (Linux; Android 13; V2244A Build/TP1A.220624.014; wv) AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 Chrome/116.0.0.0 Mobile Safari/537.36 XWEB/1160117 MMWEBSDK/20240301 MMWEBID/9871 MicroMessenger/8.0.48.2580(0x28003036) WeChat/arm64 Weixin NetType/WIFI Language/zh_CN ABI/arm64", DeviceKind.Phone, "Android", "WeChat")]
    [InlineData("Mozilla/5.0 (Phone; OpenHarmony 5.0) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/114.0.0.0 Safari/537.36 ArkWeb/4.1.6.1 Mobile HuaweiBrowser/5.0.4.300", DeviceKind.Phone, "HarmonyOS", "Chrome")]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.1 Safari/605.1.15", DeviceKind.Computer, "Mac", "Safari")]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 10.15; rv:132.0) Gecko/20100101 Firefox/132.0", DeviceKind.Computer, "Mac", "Firefox")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36 Edg/130.0.0.0", DeviceKind.Computer, "Windows", "Edge")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36 OPR/114.0.0.0", DeviceKind.Computer, "Windows", "Opera")]
    [InlineData("Mozilla/5.0 (X11; CrOS x86_64 14541.0.0) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36", DeviceKind.Computer, "ChromeOS", "Chrome")]
    [InlineData("Mozilla/5.0 (X11; Linux x86_64; rv:132.0) Gecko/20100101 Firefox/132.0", DeviceKind.Computer, "Linux", "Firefox")]
    public void NamesTheDeviceAndBrowser(string userAgent, DeviceKind kind, string name, string browser)
    {
        Assert.Equal(new DeviceInfo(kind, name, browser), UserAgents.Describe(userAgent));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("curl/8.7.1")]
    public void SaysNothingItCannotTell(string? userAgent)
    {
        Assert.Equal(DeviceInfo.Unknown, UserAgents.Describe(userAgent));
    }

    [Fact]
    public void AnAppsOwnWebViewIsAPhoneWithNoBrowser()
    {
        Assert.Equal(
            new DeviceInfo(DeviceKind.Phone, "iPhone", null),
            UserAgents.Describe("Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Mobile/15E148"));
    }
}
