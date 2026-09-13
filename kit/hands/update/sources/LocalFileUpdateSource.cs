namespace IO.Services.Update;

/// <summary>
/// 本地文件更新源 — 从本地路径或 UNC 路径读取 manifest.json + exe
/// 适用于内网/离线环境，零服务端部署
/// > ADR: 0064
/// </summary>
public sealed class LocalFileUpdateSource : IUpdateSource
{
    private readonly string _manifestPath;
    private readonly IFileSystem _fs;
    private readonly ILogger<LocalFileUpdateSource>? _logger;

    public LocalFileUpdateSource(string manifestPath, IFileSystem fs, ILogger<LocalFileUpdateSource>? logger = null)
    {
        _manifestPath = manifestPath ?? throw new ArgumentNullException(nameof(manifestPath));
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _logger = logger;
    }

    public UpdateSourceType Type => UpdateSourceType.LocalFile;

    public async Task<UpdateManifest?> GetManifestAsync(CancellationToken ct = default)
    {
        try
        {
            if (!_fs.FileExists(_manifestPath))
            {
                _logger?.LogError("LocalFileUpdateSource: 清单文件不存在 {Path}", _manifestPath);
                return null;
            }

            var json = await _fs.ReadAllTextAsync(_manifestPath, ct).ConfigureAwait(false);
            return StaticFileUpdateSource.ParseManifest(json);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "LocalFileUpdateSource: 读取清单失败 {Path}", _manifestPath);
            return null;
        }
    }

    public Task<Stream> DownloadAsync(
        UpdateManifestEntry entry,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var downloadPath = ResolveDownloadPath(entry.DownloadUrl);
        _logger?.LogDebug("LocalFileUpdateSource: 读取本地文件 {Path}", downloadPath);

        if (!_fs.FileExists(downloadPath))
            throw new FileNotFoundException($"更新文件不存在: {downloadPath}", downloadPath);

        return Task.FromResult(_fs.OpenRead(downloadPath));
    }

    /// <summary>
    /// 解析下载路径 — 相对路径解析为相对于清单目录的绝对路径
    /// </summary>
    private string ResolveDownloadPath(string downloadUrl)
    {
        if (Path.IsPathRooted(downloadUrl))
            return downloadUrl;

        var manifestDir = Path.GetDirectoryName(_manifestPath) ?? AppContext.BaseDirectory;
        return Path.Combine(manifestDir, downloadUrl);
    }
}
