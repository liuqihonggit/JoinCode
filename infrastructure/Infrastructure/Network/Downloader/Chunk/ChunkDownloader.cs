namespace Infrastructure.Network.Downloader.Chunk;

/// <summary>
/// 单分片下载器 — 用 HTTP Range 请求下载一个分片,流式写入 .part 文件
/// <para>Range 头:bytes={start+downloaded}-{end}(支持从分片中间续传)</para>
/// <para>文件 IO:通过 IFileSystem.CreateStream(FileShare.ReadWrite),符合 JCC9006</para>
/// <para>流式写入:64KB 缓冲区,避免大内存占用</para>
/// <para>chunk.Downloaded 实时更新(内存),元数据持久化由调用方负责</para>
/// </summary>
internal sealed class ChunkDownloader
{
    private const int BufferSize = 64 * 1024;

    private readonly HttpClient _httpClient;
    private readonly IFileSystem _fs;

    internal ChunkDownloader(HttpClient httpClient, IFileSystem fs)
    {
        _httpClient = httpClient;
        _fs = fs;
    }

    /// <summary>
    /// 下载单个分片到 .part 文件
    /// </summary>
    /// <param name="url">下载 URL</param>
    /// <param name="chunk">分片描述(Downloaded 字段在续传时表示已下载偏移,方法内实时更新)</param>
    /// <param name="partFilePath">.part 临时文件路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    internal async Task<ChunkDownloadResult> DownloadAsync(
        string url,
        DownloadChunk chunk,
        string partFilePath,
        CancellationToken cancellationToken = default)
    {
        var rangeStart = chunk.Start + chunk.Downloaded;
        var rangeEnd = chunk.End;

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Range = new RangeHeaderValue(rangeStart, rangeEnd);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            return new ChunkDownloadResult(false, chunk.Index, 0, $"[DOWN008] HTTP {response.StatusCode}");

        var fileMode = chunk.Downloaded > 0 ? FileMode.Append : FileMode.Create;
        var fileStream = _fs.CreateStream(partFilePath, fileMode, FileAccess.Write, FileShare.ReadWrite);
        await using var fileAsync = fileStream.ConfigureAwait(false);

        using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        var buffer = new byte[BufferSize];
        var totalRead = 0L;
        int read;

        while ((read = await responseStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            totalRead += read;
            chunk.Downloaded += read;
        }

        chunk.Completed = chunk.Downloaded == chunk.Length;
        return new ChunkDownloadResult(true, chunk.Index, totalRead, null);
    }
}
