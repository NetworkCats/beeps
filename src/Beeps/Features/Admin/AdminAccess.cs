using Beeps.Features.Shared;
using Microsoft.Extensions.Localization;

namespace Beeps.Features.Admin;

/// <summary>
/// What every admin Service does first and last: check that the caller is still an active admin,
/// and log each write in the same transaction as the change it records.
/// </summary>
public sealed class AdminAccess(IAdminStore store, TimeProvider time, IStringLocalizer<SharedResource> localizer)
{
    /// <summary>The admin check, done here rather than trusted from the page: revoking admin takes effect at once.</summary>
    public async Task<Error?> RequireAdminAsync(Guid adminId, CancellationToken cancellationToken) =>
        await store.IsActiveAdminAsync(adminId, cancellationToken)
            ? null
            : Error.Forbidden(AdminErrorCodes.NotAdmin, localizer["Error.NotAdmin"]);

    /// <summary>Stages the audit entry; it commits with the caller's next save.</summary>
    public void Audit(Guid adminId, string action, string targetType, Guid? targetId, string? detail)
    {
        var now = time.GetUtcNow();
        store.AddAuditLog(new AdminAuditLog
        {
            Id = Guid.CreateVersion7(now),
            AdminId = adminId,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            Detail = detail is null ? null : TextRules.Truncate(detail, AdminAuditLog.DetailMaxLength),
            CreatedAt = now,
        });
    }
}
