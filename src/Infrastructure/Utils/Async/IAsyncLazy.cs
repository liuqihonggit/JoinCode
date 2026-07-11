namespace Core.Utils;

public interface IAsyncLazy<T>
{
    ValueTask<T> GetValueAsync(CancellationToken ct = default);

    bool IsValueCreated { get; }
}
