
namespace Core.Utils;

/// <summary>
/// 方法名缓存 - 缓存方法名的小写形式，避免重复分配
/// </summary>
public static class MethodNameCache {
    private static ImmutableHamT<string, string> _cache = ImmutableHamT<string, string>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 获取规范化（小写）的方法名
    /// </summary>
    /// <param name="methodName">原始方法名</param>
    /// <returns>小写形式的方法名</returns>
    public static string Normalize(string methodName) {
        if (string.IsNullOrEmpty(methodName))
            return methodName;

        var snapshot = Volatile.Read(ref _cache);
        if (snapshot.TryGetValue(methodName, out var existing))
            return existing;

        var normalized = methodName.ToLowerInvariant();
        ImmutableInterlocked.Update(ref _cache, d => d.ContainsKey(methodName) ? d : d.Add(methodName, normalized));
        return Volatile.Read(ref _cache)[methodName];
    }

    /// <summary>
    /// 清除缓存
    /// </summary>
    public static void Clear() => Volatile.Write(ref _cache, ImmutableHamT<string, string>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// 获取缓存统计信息
    /// </summary>
    public static MethodNameCacheStats GetStats() {
        var count = Volatile.Read(ref _cache).Count;
        // 估算每个字符串平均占用 32 字节
        var approximateSize = count * 32;
        return new MethodNameCacheStats(count, approximateSize);
    }
}

/// <summary>
/// 方法名缓存统计 — 条目数与近似内存占用
/// </summary>
/// <param name="Count">缓存条目数</param>
/// <param name="ApproximateSize">近似内存占用(字节, 按每条约 32 字节估算)</param>
public sealed record MethodNameCacheStats(int Count, int ApproximateSize);