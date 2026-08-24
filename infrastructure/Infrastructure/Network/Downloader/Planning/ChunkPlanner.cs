namespace Infrastructure.Network.Downloader.Planning;

/// <summary>
/// 分片规划器 — 根据 ContentLength + MaxThreads + ChunkSize 计算分片列表
/// <para>纯计算,无副作用,线程安全(静态方法)</para>
/// <para>分片大小自动钳制到 [1MB, 16MB],避免过大或过小</para>
/// </summary>
internal static class ChunkPlanner
{
    /// <summary>最小分片大小:1MB(避免分片过小导致 HTTP 请求开销过大)</summary>
    internal const long MinChunkSize = 1024 * 1024;

    /// <summary>最大分片大小:16MB(避免分片过大导致单线程下载时间长)</summary>
    internal const long MaxChunkSize = 16 * 1024 * 1024;

    /// <summary>
    /// 规划分片列表
    /// </summary>
    /// <param name="contentLength">文件总字节数(必须 &gt; 0)</param>
    /// <param name="maxThreads">并发线程数(&gt;=1,1=单分片,&gt;1=多分片)</param>
    /// <param name="chunkSize">分片大小(null=自动 contentLength/maxThreads 钳制到 [1MB,16MB])</param>
    /// <returns>分片列表,按 Index 升序,连续无间隙无重叠,覆盖 [0, contentLength-1]</returns>
    /// <exception cref="ArgumentOutOfRangeException">contentLength&lt;=0 或 maxThreads&lt;1 或 chunkSize&lt;=0</exception>
    internal static IReadOnlyList<DownloadChunk> Plan(long contentLength, int maxThreads, long? chunkSize = null)
    {
        if (contentLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(contentLength), contentLength, "[DOWN007] contentLength 必须 > 0");
        if (maxThreads < 1)
            throw new ArgumentOutOfRangeException(nameof(maxThreads), maxThreads, "[DOWN003] maxThreads 必须 >= 1");
        if (chunkSize is { } cs && cs <= 0)
            throw new ArgumentOutOfRangeException(nameof(chunkSize), cs, "[DOWN004] chunkSize 必须 > 0");

        if (maxThreads == 1)
            return [CreateChunk(0, 0, contentLength - 1)];

        var size = ComputeChunkSize(contentLength, maxThreads, chunkSize);

        var chunks = new List<DownloadChunk>();
        var index = 0;
        for (var start = 0L; start < contentLength; start += size, index++)
        {
            var end = Math.Min(start + size - 1, contentLength - 1);
            chunks.Add(CreateChunk(index, start, end));
        }

        return chunks;
    }

    /// <summary>
    /// 计算分片大小 — 自动模式钳制到 [MinChunkSize, MaxChunkSize]
    /// </summary>
    private static long ComputeChunkSize(long contentLength, int maxThreads, long? chunkSize)
    {
        if (chunkSize is { } explicitSize)
            return explicitSize;

        var autoSize = contentLength / maxThreads;
        return Math.Clamp(autoSize, MinChunkSize, MaxChunkSize);
    }

    private static DownloadChunk CreateChunk(int index, long start, long end) =>
        new() { Index = index, Start = start, End = end };
}
