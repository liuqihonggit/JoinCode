namespace Core.Utils;

public interface IAsyncLazy<T> {
    /// <summary>异步获取或计算值。</summary>
    /// <param name="ct">取消令牌。</param>
    ValueTask<T> GetValueAsync(CancellationToken ct = default);

    /// <summary>获取值是否已创建。</summary>
    bool IsValueCreated { get; }
}