using Beeps.Features.Accounts;
using Beeps.Features.Beeps;

namespace Beeps.Tests.Support;

/// <summary>Beeps as the mocked stores of <see cref="BeepsWebApplicationFactory"/> return them, for the page tests.</summary>
internal static class TestBeeps
{
    public static ActorRow Human(string handle = "tom", Guid? id = null) => new(id ?? Guid.NewGuid(), ActorKind.Human, handle, handle, null);

    public static ActorRow Agent(Guid ownerId, string handle = "tom.claude", Guid? id = null) => new(id ?? Guid.NewGuid(), ActorKind.Agent, handle, "tom's Claude", ownerId);

    public static BeepRow Row(string text, ActorRow author, Guid? replyTo = null, Guid? root = null, bool liked = false, int likes = 0)
    {
        var id = Guid.CreateVersion7();
        return new BeepRow(id, text, author, replyTo, root ?? id, TestData.Now, likes, 0, liked);
    }

    /// <summary>
    /// Keeps the beep the next post adds, and lists it as by <paramref name="author"/> once it is posted, so the answer
    /// can show it. The returned function gives the posted beep, or null before the post.
    /// </summary>
    public static Func<Beep?> ArrangePosting(BeepsWebApplicationFactory factory, ActorRow author)
    {
        ArgumentNullException.ThrowIfNull(factory);
        Beep? posted = null;
        factory.Beeps.When(b => b.AddBeep(Arg.Any<Beep>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<IReadOnlyCollection<Guid>>()))
            .Do(ci => posted = ci.Arg<Beep>());
        factory.Beeps.GetRowsAsync(default!, default, CancellationToken.None).ReturnsForAnyArgs(_ =>
            posted is null ? [] : (IReadOnlyList<BeepRow>)[new BeepRow(posted.Id, posted.Text, author, null, posted.Id, TestData.Now, 0, 0, false)]);
        return () => posted;
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
