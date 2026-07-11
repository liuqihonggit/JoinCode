namespace Core.Security.Services;

[Register]
public sealed partial class SandboxModeService : ISandboxModeService, IDisposable
{
    private readonly IFileSystem _fs;
    [Inject] private readonly ILogger<SandboxModeService>? _logger;
    [Inject] private readonly IClockService _clock;
    private readonly ITelemetryService? _telemetryService;
    private readonly SemaphoreSlim _lock;
    private volatile SecuritySandboxInfo? _currentSandbox;

    public SandboxModeService(IFileSystem fs, ILogger<SandboxModeService>? logger = null, ITelemetryService? telemetryService = null, IClockService? clock = null)
    {
        _fs = fs;
        _logger = logger;
        _telemetryService = telemetryService;
        _clock = clock ?? SystemClockService.Instance;
        _lock = new SemaphoreSlim(1, 1);
    }

    public bool IsInSandbox => _currentSandbox is not null;

    public SecuritySandboxInfo? CurrentSandbox => _currentSandbox;

    public async Task<SecuritySandboxInfo> EnterSandboxAsync(SandboxOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_currentSandbox is not null)
            {
                throw new InvalidOperationException("已在沙箱中，无法嵌套进入沙箱");
            }

            var effectiveType = DetermineEffectiveSandboxType(options.Type);

            var rootPath = options.SandboxRoot
                           ?? Path.Combine(Path.GetTempPath(), "jcc-sandbox", Guid.NewGuid().ToString("N")[..12]);

            if (!_fs.DirectoryExists(rootPath))
            {
                _fs.CreateDirectory(rootPath);
            }

            var info = new SecuritySandboxInfo
            {
                Type = effectiveType,
                RootPath = rootPath,
                EnteredAt = _clock.GetUtcNow(),
                IsRestricted = options.RestrictFileSystem || options.RestrictNetwork
            };

            _currentSandbox = info;

            _logger?.LogInformation("[SandboxModeService] 进入沙箱 - 类型: {Type}, 路径: {Root}, 受限: {Restricted}",
                effectiveType, rootPath, info.IsRestricted);

            RecordSandboxMetrics("enter", effectiveType.ToString());

            return info;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ExitSandboxAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_currentSandbox is null)
            {
                _logger?.LogDebug("[SandboxModeService] 不在沙箱中，无需退出");
                return;
            }

            var sandbox = _currentSandbox;
            _currentSandbox = null;

            _logger?.LogInformation("[SandboxModeService] 退出沙箱 - 类型: {Type}, 路径: {Root}",
                sandbox.Type, sandbox.RootPath);

            RecordSandboxMetrics("exit", sandbox.Type.ToString());
        }
        finally
        {
            _lock.Release();
        }
    }

    public string ResolvePath(string path)
    {
        if (_currentSandbox is null)
        {
            return Path.GetFullPath(path);
        }

        if (!_currentSandbox.IsRestricted)
        {
            return Path.GetFullPath(path);
        }

        var fullPath = Path.GetFullPath(path);
        var sandboxRoot = Path.GetFullPath(_currentSandbox.RootPath);

        if (fullPath.StartsWith(sandboxRoot, StringComparison.OrdinalIgnoreCase))
        {
            return fullPath;
        }

        var fileName = Path.GetFileName(path);
        return Path.Combine(sandboxRoot, fileName);
    }

    private SandboxType DetermineEffectiveSandboxType(SandboxType requestedType)
    {
        if (requestedType != SandboxType.None)
        {
            return requestedType;
        }

        var envType = Environment.GetEnvironmentVariable(JccEnvVar.SandboxMode.ToValue());
        if (string.IsNullOrEmpty(envType))
        {
            return SandboxType.Process;
        }

        var parsed = SandboxTypeExtensions.FromValue(envType);
        if (parsed is not null)
        {
            return parsed.Value;
        }

        _logger?.LogWarning("[SandboxModeService] 无法解析环境变量 {EnvVar}={Value}, 使用 Process 沙箱", JccEnvVarConstants.SandboxMode, envType);
        return SandboxType.Process;
    }

    private void RecordSandboxMetrics(string operation, string type)
        => _telemetryService?.RecordCount("sandbox.operation.count", new() { ["operation"] = operation, ["type"] = type }, description: "Sandbox operation count");

    public void Dispose() => _lock.Dispose();
}
