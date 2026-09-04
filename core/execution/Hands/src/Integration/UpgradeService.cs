namespace IO.Services;

/// <summary>
/// 升级服务 — 版本检查 + 下载更新 + 应用更新（原子替换）
/// 当注入 IUpdateSource 时走更新源路径；否则回退到 GitHub API 仅做版本检查
/// > ADR: 0064
/// </summary>
[Register(typeof(IUpgradeService), ServiceLifetime.Singleton)]
public sealed partial class UpgradeService : ServiceEntity, IUpgradeService
{
    private readonly HttpClient _httpClient;
    private readonly string _repoOwner;
    private readonly string _repoName;
    private readonly IUpdateSource? _updateSource;
    private readonly IFileSystem _fs;
    private readonly ILogger<UpgradeService>? _logger;
    private Version? _cachedLatest;

    public UpgradeService(
        HttpClient httpClient,
        IFileSystem fs,
        string? repoOwner = null,
        string? repoName = null,
        IUpdateSource? updateSource = null,
        UpdateSourceConfig? updateSourceConfig = null,
        ILogger<UpgradeService>? logger = null)
    {
        _httpClient = httpClient;
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _repoOwner = repoOwner ?? JccEndpointsResolver.RepoOwner;
        _repoName = repoName ?? JccEndpointsResolver.RepoName;
        _updateSource = updateSource ?? CreateUpdateSourceFromConfig(updateSourceConfig, httpClient, fs, logger);
        _logger = logger;
    }

    /// <summary>
    /// 从配置创建 IUpdateSource — 优先用传入的 config，回退到环境变量
    /// </summary>
    private static IUpdateSource? CreateUpdateSourceFromConfig(
        UpdateSourceConfig? config,
        HttpClient httpClient,
        IFileSystem fs,
        ILogger? logger)
    {
        var effectiveConfig = config ?? CreateConfigFromEnv();
        if (effectiveConfig is null) return null;

        try
        {
            return UpdateSourceFactory.Create(effectiveConfig, httpClient, fs, logger);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 从环境变量创建 UpdateSourceConfig — JCC_UPDATE_SOURCE_TYPE + JCC_UPDATE_MANIFEST_URL
    /// </summary>
    private static UpdateSourceConfig? CreateConfigFromEnv()
    {
        var sourceTypeEnv = Environment.GetEnvironmentVariable(JccEnvVar.UpdateSourceType.ToValue());
        if (string.IsNullOrEmpty(sourceTypeEnv)) return null;

        return new UpdateSourceConfig
        {
            SourceType = sourceTypeEnv!,
            ManifestUrl = Environment.GetEnvironmentVariable(JccEnvVar.UpdateManifestUrl.ToValue()),
            Channel = Environment.GetEnvironmentVariable(JccEnvVar.UpdateChannel.ToValue()) ?? "stable",
        };
    }

    public Version GetCurrentVersion()
    {
        return typeof(UpgradeService).Assembly.GetName().Version ?? new Version(0, 1, 0);
    }

    public async Task<Version?> GetLatestVersionAsync(CancellationToken ct = default)
    {
        if (_cachedLatest != null) return _cachedLatest;

        if (_updateSource is not null)
        {
            var manifest = await _updateSource.GetManifestAsync(ct).ConfigureAwait(false);
            if (manifest is not null && Version.TryParse(manifest.LatestVersion, out var version))
            {
                _cachedLatest = version;
                return version;
            }
        }

        try
        {
            var url = $"{JccEndpointsResolver.GitHubApiBase}/repos/{_repoOwner}/{_repoName}/releases/latest";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", BrandConstants.ProductName);

            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var doc = System.Text.Json.JsonDocument.Parse(json);
            var tagName = doc.RootElement.GetProperty("tag_name").GetString();

            if (tagName != null && tagName.StartsWith('v'))
                tagName = tagName[1..];

            if (Version.TryParse(tagName, out var version))
            {
                _cachedLatest = version;
                return version;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "UpgradeService: 获取最新版本失败");
        }

        return null;
    }

    public async Task<bool> IsUpdateAvailableAsync(CancellationToken ct = default)
    {
        var latest = await GetLatestVersionAsync(ct).ConfigureAwait(false);
        return latest != null && latest > GetCurrentVersion();
    }

    /// <inheritdoc/>
    public async Task<UpdateManifestEntry?> GetUpdateEntryAsync(CancellationToken ct = default)
    {
        if (_updateSource is null)
        {
            _logger?.LogDebug("UpgradeService: 未注入 IUpdateSource，无法获取更新条目");
            return null;
        }

        try
        {
            var manifest = await _updateSource.GetManifestAsync(ct).ConfigureAwait(false);
            if (manifest is null || manifest.Releases.Count == 0)
                return null;

            var currentVersion = GetCurrentVersion();
            foreach (var entry in manifest.Releases)
            {
                if (!Version.TryParse(entry.Version, out var entryVersion))
                    continue;

                if (entryVersion > currentVersion)
                    return entry;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "UpgradeService: 获取更新条目失败");
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<UpdateResult> DownloadUpdateAsync(
        UpdateManifestEntry entry,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (_updateSource is null)
            return UpdateResult.Failed("未注入 IUpdateSource，无法下载更新");

        try
        {
            var tempDir = GetUpdateTempDirectory();
            _fs.CreateDirectory(tempDir);

            var downloadedPath = _fs.CombinePath(tempDir, $"{BrandConstants.CliCommandName}.exe.new");

            long totalRead = 0;
            {
                await using var sourceStream = await _updateSource.DownloadAsync(entry, progress, ct).ConfigureAwait(false);
                await using var fileStream = _fs.Open(downloadedPath, FileMode.Create);

                var buffer = new byte[81920];
                int read;

                while ((read = await sourceStream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    totalRead += read;

                    if (progress is not null && entry.SizeBytes > 0)
                    {
                        progress.Report(new UpdateDownloadProgress
                        {
                            BytesDownloaded = totalRead,
                            TotalBytes = entry.SizeBytes,
                            BytesPerSecond = 0
                        });
                    }
                }
            }

            var actualHash = await ComputeSha256Async(downloadedPath, ct).ConfigureAwait(false);
            if (!string.Equals(actualHash, entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                _fs.DeleteFile(downloadedPath);
                return UpdateResult.Failed($"SHA256 校验失败: 期望={entry.Sha256}, 实际={actualHash}");
            }

            _logger?.LogInformation("UpgradeService: 下载完成 {Path} ({Bytes} bytes, SHA256={Hash})", downloadedPath, totalRead, actualHash);
            return UpdateResult.Succeeded(downloadedPath: downloadedPath, requiresRestart: false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "UpgradeService: 下载更新失败");
            return UpdateResult.Failed(ex.Message);
        }
    }

    /// <inheritdoc/>
    public Task<UpdateResult> ApplyUpdateAsync(string downloadedExePath, CancellationToken ct = default)
    {
        try
        {
            if (!_fs.FileExists(downloadedExePath))
                return Task.FromResult(UpdateResult.Failed($"下载的文件不存在: {downloadedExePath}"));

            var currentExePath = Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName
                ?? throw new InvalidOperationException("无法确定当前 exe 路径");

            var backupPath = currentExePath + ".old";

            if (_fs.FileExists(backupPath))
                _fs.DeleteFile(backupPath);

            _logger?.LogInformation("UpgradeService: 备份当前 exe {Current} → {Backup}", currentExePath, backupPath);
            _fs.MoveFile(currentExePath, backupPath);

            try
            {
                _logger?.LogInformation("UpgradeService: 替换 exe {Downloaded} → {Current}", downloadedExePath, currentExePath);
                _fs.MoveFile(downloadedExePath, currentExePath);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "UpgradeService: 替换失败，回滚备份 {Backup} → {Current}", backupPath, currentExePath);
                _fs.MoveFile(backupPath, currentExePath);
                return Task.FromResult(UpdateResult.Failed($"替换失败已回滚: {ex.Message}"));
            }

            try { _fs.DeleteFile(backupPath); }
            catch (Exception ex) { _logger?.LogWarning(ex, "UpgradeService: 清理备份文件失败 {Backup}", backupPath); }

            _logger?.LogInformation("UpgradeService: 更新应用成功，需重启生效");
            return Task.FromResult(UpdateResult.Succeeded(downloadedPath: currentExePath, backupPath: backupPath, requiresRestart: true));
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "UpgradeService: 应用更新失败");
            return Task.FromResult(UpdateResult.Failed(ex.Message));
        }
    }

    /// <summary>
    /// 获取更新临时目录 — exe 同目录下的 .update 子目录（确保同卷，File.Move 同卷才原子）
    /// </summary>
    private static string GetUpdateTempDirectory()
    {
        var exePath = Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule?.FileName
            ?? AppContext.BaseDirectory;
        var exeDir = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory;
        return Path.Combine(exeDir, ".update");
    }

    /// <summary>
    /// 计算文件 SHA256（小写十六进制）
    /// </summary>
    private async Task<string> ComputeSha256Async(string filePath, CancellationToken ct)
    {
        using var sha256 = SHA256.Create();
        await using var stream = _fs.OpenRead(filePath);
        var hash = await sha256.ComputeHashAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
