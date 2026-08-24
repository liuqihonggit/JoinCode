namespace Infrastructure.Network.Downloader;

/// <summary>
/// 下载选项 — 控制并发度、断点续传、分片大小、超时等
/// <para>MaxThreads=1 时单线程顺序下载;>1 时用 PLINQ 并发分片</para>
/// </summary>
public sealed class DownloadOptions
{
    /// <summary>并发线程数:1=单线程顺序,>1=多线程 PLINQ 并发分片。默认 1</summary>
    public int MaxThreads { get; init; } = 1;

    /// <summary>是否启用断点续传:true=检查 .meta.json 并恢复,false=总是重头下载。默认 true</summary>
    public bool Resume { get; init; } = true;

    /// <summary>分片大小:null=自动(totalLength/maxThreads,钳制到 [1MB,16MB])。默认 null</summary>
    public long? ChunkSize { get; init; }

    /// <summary>单分片 HTTP 请求超时。默认 100s</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(100);

    /// <summary>期望的 Content-Length,用于校验(不匹配则报错)。null=不校验</summary>
    public long? ExpectedContentLength { get; init; }

    /// <summary>元数据持久化频率:每下载 N 字节刷新一次 .meta.json。默认 64KB</summary>
    public long MetadataFlushInterval { get; init; } = 64 * 1024;

    /// <summary>校验 MaxThreads 合法性(>=1)</summary>
    public void Validate()
    {
        if (MaxThreads < 1)
            throw new ArgumentOutOfRangeException(nameof(MaxThreads), MaxThreads, "[DOWN003] MaxThreads 必须 >= 1");
        if (ChunkSize is { } cs && cs <= 0)
            throw new ArgumentOutOfRangeException(nameof(ChunkSize), cs, "[DOWN004] ChunkSize 必须 > 0");
        if (MetadataFlushInterval <= 0)
            throw new ArgumentOutOfRangeException(nameof(MetadataFlushInterval), MetadataFlushInterval, "[DOWN005] MetadataFlushInterval 必须 > 0");
    }
}
