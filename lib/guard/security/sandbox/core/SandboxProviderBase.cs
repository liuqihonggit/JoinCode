namespace Core.Security.Sandbox;


/// <summary>
/// 沙箱提供器抽象基类 — 封装沙箱创建/销毁/路径解析的通用逻辑,子类通过钩子方法扩展特定行为
/// </summary>
public abstract class SandboxProviderBase : ISandboxProvider {
    private protected readonly IFileSystem Fs;
    private protected readonly ILogger? Logger;
    private protected readonly IClockService Clock;
    private protected readonly ITelemetryService? TelemetryService;
    private volatile ImmutableDictionary<string, SandboxInfo> _sandboxes = ImmutableDictionary<string, SandboxInfo>.Empty;
    private int _disposed;

    /// <inheritdoc/>
    public abstract SandboxType SandboxType { get; }
    /// <inheritdoc/>
    public abstract SandboxCapabilities Capabilities { get; }
    /// <inheritdoc/>
    public IEnumerable<SandboxInfo> ActiveSandboxes => _sandboxes.Values;

    /// <summary>
    /// 初始化沙箱提供器基类
    /// </summary>
    protected SandboxProviderBase(IFileSystem fs, ILogger? logger, IClockService clock, ITelemetryService? telemetryService) {
        Fs = fs;
        Logger = logger;
        Clock = clock;
        TelemetryService = telemetryService;
    }

    /// <inheritdoc/>
    public virtual bool IsAvailable => true;

    /// <inheritdoc/>
    public async Task<SandboxInfo> CreateSandboxAsync(SandboxOptions options, CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(options);

        var effectiveType = DetermineEffectiveType(options.Type);
        var sandboxId = Guid.NewGuid().ToString("N")[..12];
        var rootPath = ResolveRootPath(options, sandboxId);

        await EnsureRootPathExistsAsync(rootPath, ct).ConfigureAwait(false);

        var info = new SandboxInfo {
            Type = effectiveType,
            SandboxId = sandboxId,
            RootPath = rootPath,
            EnteredAt = Clock.GetUtcNow(),
            IsRestricted = options.RestrictFileSystem || options.RestrictNetwork,
            Capabilities = Capabilities,
            AllowedPaths = options.AllowedPaths,
            RestrictNetwork = options.RestrictNetwork,
            RestrictFileSystem = options.RestrictFileSystem
        };

        await OnCreateAsync(info, options, ct).ConfigureAwait(false);

        SetSandbox(sandboxId, info);

        Logger?.LogInformation("[Sandbox:{Type}] 创建沙箱 - Id: {Id}, 路径: {Root}, 受限: {Restricted}",
            SandboxType, sandboxId, rootPath, info.IsRestricted);

        RecordMetrics("create", SandboxType.ToValue());

        return info;
    }

    /// <inheritdoc/>
    public async Task DestroySandboxAsync(string sandboxId, CancellationToken ct = default) {
        if (!TryRemoveSandbox(sandboxId, out var info)) {
            Logger?.LogWarning("[Sandbox:{Type}] 沙箱 '{Id}' 不存在", SandboxType, sandboxId);
            return;
        }

        try {
            await OnDestroyAsync(info, ct).ConfigureAwait(false);
            Logger?.LogInformation("[Sandbox:{Type}] 销毁沙箱 - Id: {Id}", SandboxType, sandboxId);
            RecordMetrics("destroy", SandboxType.ToValue());
        } catch (Exception ex) {
            Logger?.LogError(ex, "[Sandbox:{Type}] 销毁沙箱 '{Id}' 失败", SandboxType, sandboxId);
            RecordMetrics("destroy_failed", SandboxType.ToValue());
        }
    }

    /// <inheritdoc/>
    public SandboxInfo? GetSandboxInfo(string sandboxId) {
        return _sandboxes.TryGetValue(sandboxId, out var info) ? info : null;
    }

    /// <inheritdoc/>
    public string ResolvePath(string path, string sandboxId) {
        if (!_sandboxes.TryGetValue(sandboxId, out var info)) {
            throw new InvalidOperationException($"[GRD010] 沙箱 '{sandboxId}' 不存在");
        }

        if (!info.IsRestricted) {
            return Path.GetFullPath(path);
        }

        return OnResolvePath(path, info);
    }

    /// <inheritdoc/>
    public Task<bool> IsPathInSandboxAsync(string path, string sandboxId, CancellationToken ct = default) {
        if (!_sandboxes.TryGetValue(sandboxId, out var info)) {
            return Task.FromResult(false);
        }

        var fullPath = Path.GetFullPath(path);
        var sandboxRoot = Path.GetFullPath(info.RootPath);

        if (!fullPath.StartsWith(sandboxRoot, StringComparison.OrdinalIgnoreCase)) {
            return Task.FromResult(false);
        }

        var relativePath = fullPath[sandboxRoot.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var isInSandbox = !relativePath.StartsWith("..") && !relativePath.Contains(Path.DirectorySeparatorChar + "..");

        return Task.FromResult(isInSandbox);
    }

    /// <summary>
    /// 异步释放提供器,销毁所有活跃沙箱
    /// </summary>
    public ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return ValueTask.CompletedTask;
        var snapshot = _sandboxes;
        var tasks = new List<Task>(snapshot.Count);
        foreach (var sandboxId in snapshot.Keys) {
            tasks.Add(DestroySandboxAsync(sandboxId));
        }
        return new ValueTask(Task.WhenAll(tasks));
    }

    private void SetSandbox(string sandboxId, SandboxInfo info) {
        var current = _sandboxes;
        while (true) {
            var updated = current.SetItem(sandboxId, info);
            if (Interlocked.CompareExchange(ref _sandboxes, updated, current) == current) return;
            current = _sandboxes;
        }
    }

    private bool TryRemoveSandbox(string sandboxId, out SandboxInfo info) {
        info = null!;
        var current = _sandboxes;
        while (current.ContainsKey(sandboxId)) {
            info = current[sandboxId];
            var updated = current.Remove(sandboxId);
            if (Interlocked.CompareExchange(ref _sandboxes, updated, current) == current) return true;
            current = _sandboxes;
        }
        return false;
    }

    private protected virtual SandboxType DetermineEffectiveType(SandboxType requestedType) {
        if (requestedType != SandboxType.None) {
            return requestedType;
        }

        var envType = Environment.GetEnvironmentVariable(JccEnvVar.SandboxMode.ToValue());
        if (string.IsNullOrEmpty(envType)) {
            return SandboxType;
        }

        var parsed = SandboxTypeExtensions.FromValue(envType);
        if (parsed is not null) {
            return parsed.Value;
        }

        Logger?.LogWarning("[Sandbox:{Type}] 无法解析环境变量 {EnvVar}={Value}, 使用默认沙箱类型", SandboxType, JccEnvVarEnumConstants.SandboxMode, envType);
        return SandboxType;
    }

    private protected virtual string ResolveRootPath(SandboxOptions options, string sandboxId) {
        return options.SandboxRoot
               ?? Path.Combine(Path.GetTempPath(), "jcc-sandbox", sandboxId);
    }

    private protected virtual Task EnsureRootPathExistsAsync(string rootPath, CancellationToken ct) {
        if (!Fs.DirectoryExists(rootPath)) {
            Fs.CreateDirectory(rootPath);
        }
        return Task.CompletedTask;
    }

    private protected virtual Task OnCreateAsync(SandboxInfo info, SandboxOptions options, CancellationToken ct)
        => Task.CompletedTask;

    private protected virtual Task OnDestroyAsync(SandboxInfo info, CancellationToken ct)
        => Task.CompletedTask;

    private protected virtual string OnResolvePath(string path, SandboxInfo info) {
        var fullPath = Path.GetFullPath(path);
        var sandboxRoot = Path.GetFullPath(info.RootPath);

        if (fullPath.StartsWith(sandboxRoot, StringComparison.OrdinalIgnoreCase)) {
            return fullPath;
        }

        if (info.AllowedPaths is not null) {
            foreach (var allowed in info.AllowedPaths) {
                var fullAllowed = Path.GetFullPath(allowed);
                if (fullPath.StartsWith(fullAllowed, StringComparison.OrdinalIgnoreCase)) {
                    return fullPath;
                }
            }
        }

        var fileName = Path.GetFileName(path);
        return Path.Combine(sandboxRoot, fileName);
    }

    private protected void RecordMetrics(string operation, string type)
        => TelemetryService?.RecordCount("sandbox.operation.count", new Dictionary<string, string> { ["operation"] = operation, ["type"] = type }, description: "Sandbox operation count");

    /// <inheritdoc/>
    public virtual Task<ProviderExecutionResult?> ExecuteAsync(string sandboxId, string command, string? workingDirectory, int timeoutMs, CancellationToken ct)
        => Task.FromResult<ProviderExecutionResult?>(null);
}