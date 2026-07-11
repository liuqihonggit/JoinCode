
namespace Core.Utils;

/// <summary>
/// 池化的 StringBuilder - 复用 StringBuilder 实例减少 GC 压力
/// </summary>
public sealed class PooledStringBuilder : IDisposable
{
    private static readonly ConcurrentBag<StringBuilder> Pool = new();
    private const int MaxPoolSize = 32;
    private const int MaxStringBuilderCapacity = 4096;

    /// <summary>
    /// StringBuilder 实例
    /// </summary>
    public StringBuilder Builder { get; }

    private PooledStringBuilder(StringBuilder builder)
    {
        Builder = builder;
    }

    /// <summary>
    /// 从池中租用 StringBuilder
    /// </summary>
    public static PooledStringBuilder Rent()
    {
        if (Pool.TryTake(out var builder))
        {
            builder.Clear();
            return new PooledStringBuilder(builder);
        }

        return new PooledStringBuilder(new StringBuilder(256));
    }

    /// <summary>
    /// 将 StringBuilder 返回到池中
    /// </summary>
    public void Dispose()
    {
        if (Builder.Length <= MaxStringBuilderCapacity && Pool.Count < MaxPoolSize)
        {
            Builder.Clear();
            Pool.Add(Builder);
        }
    }

    /// <summary>
    /// 获取当前内容并释放
    /// </summary>
    public string ToStringAndDispose()
    {
        var result = Builder.ToString();
        Dispose();
        return result;
    }

    /// <summary>
    /// 获取池统计信息
    /// </summary>
    public static (int Count, int MaxSize) GetStats() => (Pool.Count, MaxPoolSize);
}

/// <summary>
/// StringBuilderPool 静态访问类
/// </summary>
public static class StringBuilderPool
{
    /// <summary>
    /// 从池中租用 StringBuilder
    /// </summary>
    public static PooledStringBuilder Rent() => PooledStringBuilder.Rent();
}
