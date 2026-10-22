using Beeps.Features.Beeps;
using Beeps.Features.Translation;
using Beeps.Infrastructure.Caching;
using Microsoft.EntityFrameworkCore;

namespace Beeps.Infrastructure.Data.Stores;

public sealed class EfTranslationStore(BeepsDbContext db, PendingCacheChanges cacheChanges) : ITranslationStore
{
    public Task<TranslationSubject?> GetSubjectAsync(Guid beepId, CancellationToken cancellationToken) =>
        db.Set<Beep>()
            .AsNoTracking()
            .Where(b => b.Id == beepId)
            .Select(b => new TranslationSubject(
                b.Id,
                b.Text,
                b.ModerationStatus,
                b.DeletedAt != null,
                db.Set<BeepTranslation>().Any(t => t.BeepId == b.Id)))
            .FirstOrDefaultAsync(cancellationToken);

    public void Add(BeepTranslation translation)
    {
        ArgumentNullException.ThrowIfNull(translation);
        db.Set<BeepTranslation>().Add(translation);
        // The beep's cached copy carries its translations.
        cacheChanges.Add(CacheChange.Beep(translation.BeepId));
    }
}
