namespace Core.Utils;

public static class TimeoutHelper
{
    public static CancellationTokenSource CreateLinkedTimeout(CancellationToken ct, TimeSpan timeout)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        return cts;
    }

    public static async Task WithTimeoutAsync(
        Func<CancellationToken, Task> operation,
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        using var cts = CreateLinkedTimeout(ct, timeout);
        try
        {
            await operation(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"操作在 {timeout.TotalMilliseconds}ms 内未完成");
        }
    }

    public static async Task<T> WithTimeoutAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        using var cts = CreateLinkedTimeout(ct, timeout);
        try
        {
            return await operation(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"操作在 {timeout.TotalMilliseconds}ms 内未完成");
        }
    }
}
