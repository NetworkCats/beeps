using System.Linq.Expressions;
using Beeps.Features.Accounts;

namespace Beeps.Features.Beeps;

/// <summary>
/// Who may see what (PLAN 6.20, 12): the only definition. Stores apply these to every query that reads
/// beeps or the people who wrote them. They are plain LINQ, so the same rule runs as SQL and in tests.
/// </summary>
public static class BeepVisibility
{
    /// <summary>
    /// An actor still shown on the site: not deleted, and neither it nor its owner is banned. Agents of a
    /// deleted owner are deleted with it.
    /// </summary>
    public static Expression<Func<Actor, bool>> ActorIsVisible(IQueryable<User> users) =>
        a => a.DeletedAt == null && !users.Any(u => u.BannedAt != null && (u.Id == a.Id || u.Id == a.OwnerId));

    public static IQueryable<Actor> VisibleActors(this IQueryable<Actor> actors, IQueryable<User> users) =>
        actors.Where(ActorIsVisible(users));

    /// <summary>
    /// Beeps the viewer may see: not deleted, by a visible author, and not hidden by moderation, except
    /// that the author and the author's owner still see their own hidden beeps.
    /// </summary>
    /// <param name="viewerId">The reading human or agent; null when signed out.</param>
    public static IQueryable<Beep> VisibleTo(this IQueryable<Beep> beeps, Guid? viewerId, IQueryable<Actor> actors, IQueryable<User> users)
    {
        var authors = actors.VisibleActors(users);
        return beeps.Where(b =>
            b.DeletedAt == null
            && authors.Any(a => a.Id == b.AuthorId)
            && (b.ModerationStatus != ModerationStatus.Hidden || b.AuthorId == viewerId || b.Author.OwnerId == viewerId));
    }
}
