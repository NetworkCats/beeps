namespace Beeps.Features.Shared;

public static class Batches
{
    /// <summary>
    /// Runs <paramref name="deleteBatch"/>, which removes at most <paramref name="batchSize"/> rows, until a batch comes
    /// back short; returns how many went in all.
    /// </summary>
    public static async Task<int> DrainAsync(int batchSize, Func<CancellationToken, Task<int>> deleteBatch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(deleteBatch);
        var total = 0;
        int deleted;
        do
        {
            deleted = await deleteBatch(cancellationToken);
            total += deleted;
        }
        while (deleted >= batchSize);

        return total;
    }
}
