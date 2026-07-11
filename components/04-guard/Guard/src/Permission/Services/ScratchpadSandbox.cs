namespace Core.Permission.Services;

[Register]
public sealed partial class ScratchpadSandbox : IScratchpadSandbox
{
    private readonly ConcurrentDictionary<string, SandboxInfo> _sandboxes;
    private readonly IFileOperationService _fileOperationService;
    [Inject] private readonly ILogger<ScratchpadSandbox>? _logger;
    [Inject] private readonly IClockService _clock;
    private readonly ITelemetryService? _telemetryService;

    public ScratchpadSandbox(
        IFileOperationService fileOperationService,
        ILogger<ScratchpadSandbox>? logger = null,
        ITelemetryService? telemetryService = null,
        IClockService? clock = null)
    {
        _fileOperationService = fileOperationService ?? throw new ArgumentNullException(nameof(fileOperationService));
        _sandboxes = new ConcurrentDictionary<string, SandboxInfo>();
        _logger = logger;
        _telemetryService = telemetryService;
        _clock = clock ?? SystemClockService.Instance;
    }

    public async Task<string> CreateSandboxAsync(string? basePath = null, CancellationToken ct = default)
    {
        var sandboxId = Guid.NewGuid().ToString("N")[..12];
        var rootPath = Path.Combine(basePath ?? Path.GetTempPath(), "jcc-sandbox", sandboxId);

        if (!_fileOperationService.DirectoryExists(rootPath))
        {
            _fileOperationService.CreateDirectory(rootPath);
        }

        var info = new SandboxInfo
        {
            SandboxId = sandboxId,
            RootPath = rootPath,
            CreatedAt = _clock.GetUtcNow(),
            SizeBytes = 0
        };

        _sandboxes[sandboxId] = info;

        _logger?.LogInformation("[ScratchpadSandbox] 创建沙箱: {SandboxId}, 路径: {RootPath}", sandboxId, rootPath);
        RecordSandboxMetrics("create", true);

        await Task.CompletedTask.ConfigureAwait(false);
        return sandboxId;
    }

    public Task<bool> IsPathInSandboxAsync(string path, string sandboxId, CancellationToken ct = default)
    {
        if (!_sandboxes.TryGetValue(sandboxId, out var info))
        {
            return Task.FromResult(false);
        }

        var fullPath = Path.GetFullPath(path);
        var sandboxRoot = Path.GetFullPath(info.RootPath);

        var isInSandbox = fullPath.StartsWith(sandboxRoot, StringComparison.OrdinalIgnoreCase);

        if (isInSandbox)
        {
            var relativePath = fullPath[sandboxRoot.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            isInSandbox = !relativePath.StartsWith("..") && !relativePath.Contains(Path.DirectorySeparatorChar + "..");
        }

        return Task.FromResult(isInSandbox);
    }

    public Task<string> ResolveSandboxPathAsync(string path, string sandboxId, CancellationToken ct = default)
    {
        if (!_sandboxes.TryGetValue(sandboxId, out var info))
        {
            throw new InvalidOperationException($"沙箱 '{sandboxId}' 不存在");
        }

        var sanitizedPath = path.Replace('\\', '/').TrimStart('/');

        var segments = sanitizedPath.Split('/');
        var safeSegments = new List<string>();
        foreach (var segment in segments)
        {
            if (segment == ".." || segment == ".")
            {
                continue;
            }

            if (!string.IsNullOrEmpty(segment))
            {
                safeSegments.Add(segment);
            }
        }

        var safeRelativePath = string.Join(Path.DirectorySeparatorChar, safeSegments);
        var resolvedPath = Path.GetFullPath(Path.Combine(info.RootPath, safeRelativePath));

        var sandboxRoot = Path.GetFullPath(info.RootPath);
        if (!resolvedPath.StartsWith(sandboxRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException($"路径遍历攻击检测: '{path}' 尝试逃出沙箱 '{sandboxId}'");
        }

        return Task.FromResult(resolvedPath);
    }

    public async Task DestroySandboxAsync(string sandboxId, CancellationToken ct = default)
    {
        if (!_sandboxes.TryRemove(sandboxId, out var info))
        {
            _logger?.LogWarning("[ScratchpadSandbox] 沙箱 '{SandboxId}' 不存在", sandboxId);
            return;
        }

        try
        {
            if (_fileOperationService.DirectoryExists(info.RootPath))
            {
                var files = await _fileOperationService.ListDirectoryAsync(info.RootPath, recursive: true, cancellationToken: ct).ConfigureAwait(false);

                foreach (var file in files.Files)
                {
                    await _fileOperationService.DeleteFileAsync(file.FullPath, ct).ConfigureAwait(false);
                }

                _logger?.LogInformation("[ScratchpadSandbox] 已销毁沙箱: {SandboxId}", sandboxId);
                RecordSandboxMetrics("destroy", true);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[ScratchpadSandbox] 销毁沙箱 '{SandboxId}' 失败", sandboxId);
            RecordSandboxMetrics("destroy", false);
        }
    }

    private void RecordSandboxMetrics(string operation, bool isSuccess)
        => _telemetryService?.RecordCount("sandbox.operation.count", new() { ["operation"] = operation, ["success"] = isSuccess.ToString() }, description: "Sandbox operation count");

    public SandboxInfo GetSandboxInfo(string sandboxId)
    {
        if (!_sandboxes.TryGetValue(sandboxId, out var info))
        {
            throw new KeyNotFoundException($"沙箱 '{sandboxId}' 不存在");
        }

        return info;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var sandboxId in _sandboxes.Keys.ToList())
        {
            await DestroySandboxAsync(sandboxId).ConfigureAwait(false);
        }
    }
}
