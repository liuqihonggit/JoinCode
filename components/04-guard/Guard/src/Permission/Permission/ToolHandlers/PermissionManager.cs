
namespace Core.Permission;

/// <summary>
/// 权限管理器实现，提供线程安全的权限检查和缓存机制
/// </summary>
[Register]
public sealed partial class PermissionManager : IToolPermissionManager, IAsyncDisposable
{
    private readonly PermissionChecker _permissionChecker;
    [Inject] private readonly ILogger<PermissionManager>? _logger;
    private readonly ConcurrentDictionary<string, CachedPermissionResult> _permissionCache;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _approvedTools;
    private readonly SemaphoreSlim _modeLock;
    private readonly PermissionConfig _config;
    private readonly TimeProvider _timeProvider;
    private PermissionMode _currentMode;
    private List<ToolPermissionRule>? _strippedRules;
    private bool _disposed;

    /// <summary>
    /// 缓存过期时间
    /// </summary>
    public TimeSpan CacheExpiration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 创建权限管理器
    /// </summary>
    /// <param name="permissionChecker">权限检查器</param>
    /// <param name="configOptions">权限配置选项</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="timeProvider">时间提供器，用于控制时间推进（测试可注入 FakeTimeProvider）</param>
    /// <param name="fs">文件系统（用于检查 settings.json 中的 disableBypassPermissionsMode）；DI 自动注入，测试可省略</param>
    public PermissionManager(PermissionChecker permissionChecker, IOptions<PermissionConfig> configOptions, ILogger<PermissionManager>? logger = null, TimeProvider? timeProvider = null, IFileSystem? fs = null)
    {
        _config = configOptions.Value;
        _permissionChecker = permissionChecker;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _permissionCache = new ConcurrentDictionary<string, CachedPermissionResult>();
        _approvedTools = new ConcurrentDictionary<string, DateTimeOffset>();
        _modeLock = new SemaphoreSlim(1, 1);
        _currentMode = PermissionChecker.TryGetPermissionModeFromEnv(fs) ?? PermissionMode.Default;
    }

    /// <inheritdoc />
    public async Task<PermissionResult> CheckPermissionAsync(PermissionRequest request, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        ArgumentNullException.ThrowIfNull(request);

        _logger?.LogDebug("检查权限: Tool={ToolName}, RequestId={RequestId}",
            request.ToolName, request.RequestId);

        var cacheKey = GenerateCacheKey(request);

        if (TryGetCachedResult(cacheKey, out var cachedResult))
        {
            _logger?.LogDebug("使用缓存的权限结果: Tool={ToolName}, IsGranted={IsGranted}",
                request.ToolName, cachedResult.IsGranted);
            return cachedResult;
        }

        PermissionMode currentMode;
        await _modeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            currentMode = _currentMode;
        }
        finally
        {
            _modeLock.Release();
        }

        if (IsToolTemporarilyApproved(request.ToolName))
        {
            var grantedResult = PermissionResult.Granted();
            CacheResult(cacheKey, grantedResult);
            return grantedResult;
        }

        var checkResult = await _permissionChecker.CheckPermissionAsync(request.ToolName, request.Arguments, cancellationToken).ConfigureAwait(false);

        PermissionResult result;
        if (checkResult.IsApproved)
        {
            result = PermissionResult.Granted();
            _logger?.LogInformation("权限已批准: Tool={ToolName}, RequestId={RequestId}",
                request.ToolName, request.RequestId);
        }
        else if (checkResult.ConfirmationRequired)
        {
            result = PermissionResult.PendingConfirmation(checkResult.Reason ?? $"工具 '{request.ToolName}' 需要确认");
            _logger?.LogInformation("权限需要确认: Tool={ToolName}, RequestId={RequestId}, Reason={Reason}",
                request.ToolName, request.RequestId, checkResult.Reason);
        }
        else
        {
            result = PermissionResult.Denied(checkResult.Reason ?? "权限被拒绝");
            _logger?.LogWarning("权限被拒绝: Tool={ToolName}, RequestId={RequestId}, Reason={Reason}",
                request.ToolName, request.RequestId, checkResult.Reason);
        }

        CacheResult(cacheKey, result);

        return result;
    }

    /// <inheritdoc />
    public async Task SetPermissionModeAsync(PermissionMode mode, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _modeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _currentMode = mode;
            _permissionChecker.CurrentMode = mode;
        }
        finally
        {
            _modeLock.Release();
        }

        ClearCache();

        _logger?.LogInformation("权限模式已切换: {Mode}", mode);
    }

    /// <inheritdoc />
    public async Task<PermissionMode> GetCurrentModeAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _modeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _currentMode;
        }
        finally
        {
            _modeLock.Release();
        }
    }

    /// <inheritdoc />
    public Task AddAllowedPromptAsync(string prompt, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // 对齐 TS allowedPrompts: 将语义级Bash权限注册为临时批准
        // prompt 是语义描述（如"run tests"），映射到 Bash 工具的临时批准
        // 当前实现：将 prompt 作为 Bash 工具的临时批准，有效期 30 分钟
        ApproveToolTemporarily(ShellToolNameConstants.Bash, TimeSpan.FromMinutes(30));

        _logger?.LogInformation("已添加语义级Bash权限: Prompt={Prompt}", prompt);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 临时批准工具（在指定时间内自动批准）
    /// </summary>
    /// <param name="toolName">工具名称</param>
    /// <param name="duration">批准持续时间</param>
    public void ApproveToolTemporarily(string toolName, TimeSpan duration)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var expirationTime = _timeProvider.GetUtcNow().Add(duration);
        _approvedTools.AddOrUpdate(toolName, expirationTime, (_, _) => expirationTime);

        _logger?.LogInformation("工具已临时批准: Tool={ToolName}, Expiration={Expiration}",
            toolName, expirationTime);
    }

    /// <summary>
    /// 移除工具的临时批准
    /// </summary>
    /// <param name="toolName">工具名称</param>
    public void RemoveTemporaryApproval(string toolName)
    {
        _approvedTools.TryRemove(toolName, out _);
        _logger?.LogInformation("工具临时批准已移除: Tool={ToolName}", toolName);
    }

    /// <summary>
    /// 清除所有缓存
    /// </summary>
    public void ClearCache()
    {
        _permissionCache.Clear();
        _logger?.LogDebug("权限缓存已清除");
    }

    /// <summary>
    /// 清理过期的缓存项
    /// </summary>
    public void CleanupExpiredCache()
    {
        var now = _timeProvider.GetUtcNow();

        var expiredKeys = _permissionCache
            .Where(kvp => kvp.Value.IsExpired(now))
            .Select(kvp => kvp.Key)
            .ToList();
        expiredKeys.ForEach(key => _permissionCache.TryRemove(key, out _));

        var expiredTools = _approvedTools
            .Where(kvp => kvp.Value <= now)
            .Select(kvp => kvp.Key)
            .ToList();
        expiredTools.ForEach(tool => _approvedTools.TryRemove(tool, out _));

        if (expiredKeys.Count > 0 || expiredTools.Count > 0)
        {
            _logger?.LogDebug("已清理过期缓存项: Cache={CacheCount}, Tools={ToolCount}",
                expiredKeys.Count, expiredTools.Count);
        }
    }

    /// <inheritdoc />
    public Task<int> StripDangerousRulesAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // 对齐 TS stripDangerousPermissionsForAutoMode:
        // 从 AutoApprovedTools 中移除危险工具（Bash, FileEdit, FileWrite, NotebookEdit 等）
        var dangerousToolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ShellToolNameConstants.Bash, FileToolNameConstants.FileEdit, FileToolNameConstants.FileWrite, NotebookToolNameConstants.NotebookEdit
        };

        var strippedRules = _config.AutoApprovedTools
            .Where(r => dangerousToolNames.Contains(r.ToolName))
            .ToList();

        foreach (var rule in strippedRules)
        {
            _config.AutoApprovedTools.Remove(rule);
        }

        // 保存剥离的规则，供恢复时使用
        _strippedRules = strippedRules;

        if (strippedRules.Count > 0)
        {
            _logger?.LogInformation("已剥离 {Count} 条危险权限规则", strippedRules.Count);
        }

        return Task.FromResult(strippedRules.Count);
    }

    /// <inheritdoc />
    public Task RestoreDangerousRulesAsync(int ruleCount, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // 对齐 TS restoreDangerousPermissions: 恢复之前剥离的危险权限规则
        if (_strippedRules is { Count: > 0 })
        {
            _config.AutoApprovedTools.AddRange(_strippedRules);
            _logger?.LogInformation("已恢复 {Count} 条危险权限规则", _strippedRules.Count);
            _strippedRules = null;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _modeLock.Dispose();
        _permissionCache.Clear();
        _approvedTools.Clear();

        GC.SuppressFinalize(this);
    }

    #region Private Methods

    private string GenerateCacheKey(PermissionRequest request)
    {
        if (request.Arguments == null || request.Arguments.Count == 0)
        {
            return $"{request.ToolName}:noargs";
        }

        var keyParams = request.Arguments
            .Where(kvp => IsKeyParameter(kvp.Key))
            .OrderBy(kvp => kvp.Key)
            .Select(kvp => $"{kvp.Key}={kvp.Value}");

        return $"{request.ToolName}:{string.Join("|", keyParams)}";
    }

    private static bool IsKeyParameter(string paramName)
    {
        var keyParams = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "path",
            "command",
            "operation",
            "action"
        };

        return keyParams.Contains(paramName);
    }

    private bool TryGetCachedResult(string cacheKey, [NotNullWhen(true)] out PermissionResult? result)
    {
        result = null;

        if (!_permissionCache.TryGetValue(cacheKey, out var cached))
        {
            return false;
        }

        if (cached.IsExpired(_timeProvider.GetUtcNow()))
        {
            _permissionCache.TryRemove(cacheKey, out _);
            return false;
        }

        result = cached.Result;
        return true;
    }

    private void CacheResult(string cacheKey, PermissionResult result)
    {
        if (result.RequiresConfirmation)
        {
            return;
        }

        var cached = new CachedPermissionResult(result, _timeProvider.GetUtcNow().Add(CacheExpiration));
        _permissionCache.AddOrUpdate(cacheKey, cached, (_, _) => cached);
    }

    private bool IsToolTemporarilyApproved(string toolName)
    {
        if (!_approvedTools.TryGetValue(toolName, out var expirationTime))
        {
            return false;
        }

        if (_timeProvider.GetUtcNow() > expirationTime)
        {
            _approvedTools.TryRemove(toolName, out _);
            return false;
        }

        return true;
    }

    #endregion

    /// <summary>
    /// 缓存的权限结果
    /// </summary>
    private sealed class CachedPermissionResult
    {
        public PermissionResult Result { get; }
        public DateTimeOffset ExpirationTime { get; }

        public CachedPermissionResult(PermissionResult result, DateTimeOffset expirationTime)
        {
            Result = result;
            ExpirationTime = expirationTime;
        }

        public bool IsExpired(DateTimeOffset now) => now > ExpirationTime;
    }
}
