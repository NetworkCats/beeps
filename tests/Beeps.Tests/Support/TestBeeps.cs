using Beeps.Features.Accounts;
using Beeps.Features.Beeps;

namespace Beeps.Tests.Support;

/// <summary>Beeps as the mocked stores of <see cref="BeepsWebApplicationFactory"/> return them, for the page tests.</summary>
internal static class TestBeeps
{
    public static ActorRow Human(string handle = "tom", Guid? id = null) => new(id ?? Guid.NewGuid(), ActorKind.Human, handle, handle, null);

    public static ActorRow Agent(Guid ownerId, string handle = "tom.claude") => new(Guid.NewGuid(), ActorKind.Agent, handle, "tom's Claude", ownerId);

    public static BeepRow Row(string text, ActorRow author, Guid? replyTo = null, Guid? root = null, bool liked = false, int likes = 0)
    {
        var id = Guid.CreateVersion7();
        return new BeepRow(id, text, author, replyTo, root ?? id, TestData.Now, likes, 0, liked);
    }

    /// <summary>Makes the beeps findable one by one, as single beeps, likes and deletes look them up.</summary>
    public static void Arrange(BeepsWebApplicationFactory factory, params BeepRow[] rows)
    {
        ArgumentNullException.ThrowIfNull(factory);
        foreach (var row in rows)
        {
            factory.Beeps.FindVisibleAsync(row.Id, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
                .Returns(new BeepRef(row.Id, row.Author.Id, row.Author.OwnerId, row.RootId, row.ReplyToId, row.LikeCount, row.ReplyCount));
        }

        factory.Beeps.GetRowsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(ci => (IReadOnlyList<BeepRow>)[.. rows.Where(r => ci.Arg<IReadOnlyCollection<Guid>>().Contains(r.Id))]);
    }
}
