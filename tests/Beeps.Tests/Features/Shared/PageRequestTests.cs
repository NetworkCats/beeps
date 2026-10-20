using Beeps.Features.Shared;
using Beeps.Tests.Support;

namespace Beeps.Tests.Features.Shared;

public sealed class PageRequestTests
{
    [Fact]
    public void PollingLeavesOutWhatMayStillBeCommitted()
    {
        var page = PageRequest.Create(sinceId: Guid.CreateVersion7(TestData.Now.AddMinutes(-1))).Settled(TestData.Now);
        var settled = Guid.CreateVersion7(TestData.Now - PageRequest.SettleTime - TimeSpan.FromMilliseconds(1));
        var young = Guid.CreateVersion7(TestData.Now - PageRequest.SettleTime);

        // As PostgreSQL orders uuids: byte by byte, which is the order of their text.
        var until = page.Until!.Value.ToString();
        Assert.True(string.CompareOrdinal(settled.ToString(), until) < 0);
        Assert.True(string.CompareOrdinal(young.ToString(), until) > 0);
    }

    [Fact]
    public void TheFirstPageIsCutToo()
    {
        // Its newest id is what the first poll's since_id names: a late one below it would never come.
        Assert.NotNull(PageRequest.Create().Settled(TestData.Now).Until);
    }

    [Fact]
    public void PagingBackIsNotCut() =>
        Assert.Null(PageRequest.Create(before: Guid.CreateVersion7(), sinceId: Guid.CreateVersion7()).Settled(TestData.Now).Until);
}
