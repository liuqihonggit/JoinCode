namespace IO.Services.Update;

/// <summary>
/// 本地文件更新源 — 从本地路径或 UNC 路径读取 manifest.json + exe
/// > ADR: 0064
/// </summary>
public sealed class LocalFileUpdateSource : IUpdateSource
{
    private readonly string _manifestPath;
    private readonly ILogger<LocalFileUpdateSource>? _logger;

    public LocalFileUpdateSource(string manifestPath, ILogger<LocalFileUpdateSource>? logger = null)
    {
        _manifestPath = manifestPath ?? throw new ArgumentNullException(nameof(manifestPath));
        _logger = logger;
    }

    public UpdateSourceType Type => UpdateSourceType.LocalFile;

    public Task<UpdateManifest?> GetManifestAsync(CancellationToken ct = default)
    {
        throw new NotImplementedException("ADR 0064: LocalFileUpdateSource.GetManifestAsync 待实现");
    }

    public Task<Stream> DownloadAsync(
        UpdateManifestEntry entry,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        throw new NotImplementedException("ADR 0064: LocalFileUpdateSource.DownloadAsync 待实现");
    }
}
