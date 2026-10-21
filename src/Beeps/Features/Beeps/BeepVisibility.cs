using System.Linq.Expressions;
using Beeps.Features.Accounts;
using Beeps.Features.Profiles;

namespace Beeps.Features.Beeps;

/// <summary>
/// Who may see what: the only definition. Stores apply these to every query that reads
/// beeps or the people who wrote them. They are plain LINQ, so the same rule runs as SQL and in tests.
/// </summary>
/// <remarks>
/// Written so PostgreSQL answers each part by index for the rows at hand: one primary-key probe per condition, never a
/// pass over every actor or every banned user. Timelines read a handful of rows out of millions.
/// </remarks>
public static class BeepVisibility
{
    /// <summary>
    /// An actor still shown on the site: not deleted, and neither it nor its owner is banned. Agents of a
    /// deleted owner are deleted with it.
    /// </summary>
    public static Expression<Func<Actor, bool>> ActorIsVisible(IQueryable<User> users) =>
        a => a.DeletedAt == null
            && !users.Any(u => u.Id == a.Id && u.BannedAt != null)
            && (a.OwnerId == null || !users.Any(u => u.Id == a.OwnerId && u.BannedAt != null));

    public static IQueryable<Actor> VisibleActors(this IQueryable<Actor> actors, IQueryable<User> users) =>
        actors.Where(ActorIsVisible(users));

    /// <summary>
    /// Leaves out the actors the viewer blocked; blocking a human leaves out their agents too. Blocks
    /// belong to humans, so an agent reads with its owner's. Signed out, nobody is left out.
    /// </summary>
    /// <param name="all">Every actor, to find the viewer's owner.</param>
    public static IQueryable<Actor> NotBlockedBy(this IQueryable<Actor> actors, Guid? viewerId, IQueryable<Actor> all, IQueryable<Block> blocks) =>
        viewerId is { } viewer ? actors.Where(NotBlocked(viewer, all, blocks)) : actors;

    /// <summary>
    /// What the viewer may see of a beep itself, whoever wrote it: not deleted, and not hidden by moderation, except
    /// that the author and the author's owner still see their own hidden beeps. Queries that already know the author
    /// is visible (<see cref="VisibleActors"/>, <see cref="NotBlockedBy"/>) need only this.
    /// </summary>
    /// <remarks>
    /// The owner is only looked up for hidden beeps, which are few. Signed out there is no exception at all: compared
    /// with a null viewer, a human author's empty owner would count as a match (C# null semantics, which EF keeps).
    /// </remarks>
    public static Expression<Func<Beep, bool>> IsShownTo(Guid? viewerId, IQueryable<Actor> actors)
    {
        if (viewerId is not { } viewer)
        {
            return b => b.DeletedAt == null && b.ModerationStatus != ModerationStatus.Hidden;
        }

        return b => b.DeletedAt == null
            && (b.ModerationStatus != ModerationStatus.Hidden
                || b.AuthorId == viewer
                || actors.Any(o => o.Id == b.AuthorId && o.OwnerId == viewer));
    }

    /// <summary>
    /// Beeps the viewer may see: <see cref="IsShownTo"/>, by a visible author (<see cref="ActorIsVisible"/>) the
    /// viewer has not blocked (<see cref="NotBlockedBy"/>).
    /// </summary>
    /// <param name="viewerId">The reading human or agent; null when signed out.</param>
    public static IQueryable<Beep> VisibleTo(this IQueryable<Beep> beeps, Guid? viewerId, IQueryable<Actor> actors, IQueryable<User> users, IQueryable<Block> blocks)
    {
        beeps = beeps.Where(IsShownTo(viewerId, actors)).Where(OnAuthor(ActorIsVisible(users)));
        return viewerId is { } viewer ? beeps.Where(OnAuthor(NotBlocked(viewer, actors, blocks))) : beeps;
    }

    private static Expression<Func<Actor, bool>> NotBlocked(Guid viewer, IQueryable<Actor> all, IQueryable<Block> blocks)
    {
        var blocked = blocks
            .Where(k => k.BlockerId == viewer || k.BlockerId == all.Where(v => v.Id == viewer).Select(v => v.OwnerId).FirstOrDefault())
            .Select(k => k.BlockedId);
        return a => !blocked.Contains(a.Id) && (a.OwnerId == null || !blocked.Contains(a.OwnerId.Value));
    }

    /// <summary>A condition on an actor, applied to a beep's author: one join to the author, however many conditions.</summary>
    private static Expression<Func<Beep, bool>> OnAuthor(Expression<Func<Actor, bool>> condition)
    {
        var beep = Expression.Parameter(typeof(Beep), "b");
        var author = Expression.Property(beep, nameof(Beep.Author));
        return Expression.Lambda<Func<Beep, bool>>(new Substitution(condition.Parameters[0], author).Visit(condition.Body), beep);
    }

    private sealed class Substitution(ParameterExpression parameter, Expression replacement) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == parameter ? replacement : base.VisitParameter(node);
    }
}
