namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 非托管内存资源条目 — 明确登记的非托管资源
/// <para>持有 SafeHandle,卸载时逐个释放确保无泄漏</para>
/// </summary>
public sealed class UnmanagedResourceEntry
{
    /// <summary>资源键</summary>
    public string Key { get; }

    /// <summary>SafeHandle 包装的非托管资源</summary>
    public SafeHandle Handle { get; }

    /// <summary>估计占用字节数 — 用于泄漏报告</summary>
    public long EstimatedBytes { get; }

    /// <summary>登记时刻</summary>
    public DateTime RegisteredAt { get; }

    /// <summary>创建非托管资源条目</summary>
    public UnmanagedResourceEntry(string key, SafeHandle handle, long estimatedBytes)
    {
        Key = key ?? throw new ArgumentNullException(nameof(key));
        Handle = handle ?? throw new ArgumentNullException(nameof(handle));
        EstimatedBytes = estimatedBytes;
        RegisteredAt = DateTime.UtcNow;
    }
}
