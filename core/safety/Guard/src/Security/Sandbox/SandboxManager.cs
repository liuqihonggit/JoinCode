namespace Core.Security.Sandbox;

using JoinCode.Abstractions.Security.Sandbox;
using Providers;

[Register]
public sealed partial class SandboxManager : ISandboxManager, IDisposable
{
    private readonly FrozenDictionary<SandboxType, ISandboxProvider> _providers;
    private readonly AsyncLock _lock = new();
    [Inject] private readonly ILogger<SandboxManager>? _logger;
    private volatile ISandboxProvider? _activeProvider;
    private volatile string? _activeSandboxId;

    public SandboxManager(
        IEnumerable<ISandboxProvider> providers,
        ILogger<SandboxManager>? logger = null)
    {
        _logger = logger;
        _providers = providers
            .Where(p => p.IsAvailable)
            .ToFrozenDictionary(p => p.SandboxType, p => p);

        _logger?.LogInformation("[SandboxManager] 可用沙箱类型: {Types}", string.Join(", ", _providers.Keys.Select(k => k.ToValue())));
    }

    public ISandboxProvider? ActiveProvider => _activeProvider;

    public SandboxType ActiveSandboxType => _activeProvider?.SandboxType ?? SandboxType.None;

    public bool IsInSandbox => _activeProvider is not null && _activeSandboxId is not null && _activeProvider.GetSandboxInfo(_activeSandboxId) is not null;

    public SandboxInfo? CurrentSandbox => _activeSandboxId is not null ? _activeProvider?.GetSandboxInfo(_activeSandboxId) : null;

    public string? CurrentSandboxId => _activeSandboxId;

    public IReadOnlyList<SandboxType> AvailableTypes => [.. _providers.Keys];

    public async Task<SandboxInfo> EnterSandboxAsync(SandboxOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        using (await _lock.LockAsync(ct).ConfigureAwait(false))
        {
            if (IsInSandbox)
            {
                throw new InvalidOperationException($"已在 {_activeProvider!.SandboxType} 沙箱中，请先退出再进入新沙箱");
            }

            var provider = ResolveProvider(options.Type);
            var info = await provider.CreateSandboxAsync(options, ct).ConfigureAwait(false);

            _activeProvider = provider;
            _activeSandboxId = info.SandboxId;

            _logger?.LogInformation("[SandboxManager] 沙箱已激活 - 类型: {Type}, Id: {Id}", info.Type, info.SandboxId);

            return info;
        }
    }

    public async Task ExitSandboxAsync(CancellationToken ct = default)
    {
        using (await _lock.LockAsync(ct).ConfigureAwait(false))
        {
            if (_activeProvider is null || _activeSandboxId is null)
            {
                _logger?.LogDebug("[SandboxManager] 不在沙箱中，无需退出");
                return;
            }

            var provider = _activeProvider;
            var sandboxId = _activeSandboxId;

            await provider.DestroySandboxAsync(sandboxId, ct).ConfigureAwait(false);

            _activeProvider = null;
            _activeSandboxId = null;

            _logger?.LogInformation("[SandboxManager] 沙箱已退出 - 类型: {Type}", provider.SandboxType);
        }
    }

    public async Task SwitchProviderAsync(SandboxType type, CancellationToken ct = default)
    {
        using (await _lock.LockAsync(ct).ConfigureAwait(false))
        {
            var previousType = _activeProvider?.SandboxType ?? SandboxType.None;
            var previousInfo = CurrentSandbox;

            if (_activeProvider is not null && _activeSandboxId is not null)
            {
                await _activeProvider.DestroySandboxAsync(_activeSandboxId, ct).ConfigureAwait(false);
            }

            var newProvider = ResolveProvider(type);

            var newOptions = new SandboxOptions
            {
                Type = type,
                RestrictNetwork = previousInfo?.RestrictNetwork ?? true,
                RestrictFileSystem = previousInfo?.RestrictFileSystem ?? true,
                AllowedPaths = previousInfo?.AllowedPaths,
                SandboxRoot = previousInfo?.RootPath,
                MemoryLimitMb = 0,
                CpuLimitPercent = 0,
                TimeLimitSeconds = 0
            };

            var newInfo = await newProvider.CreateSandboxAsync(newOptions, ct).ConfigureAwait(false);

            _activeProvider = newProvider;
            _activeSandboxId = newInfo.SandboxId;

            _logger?.LogInformation("[SandboxManager] 沙箱切换: {From} → {To}, 新 Id: {Id}",
                previousType.ToValue(), type.ToValue(), newInfo.SandboxId);
        }
    }

    public ISandboxProvider? GetProvider(SandboxType type)
    {
        return _providers.TryGetValue(type, out var provider) ? provider : null;
    }

    public string ResolvePath(string path)
    {
        if (_activeProvider is null || _activeSandboxId is null)
        {
            return Path.GetFullPath(path);
        }

        return _activeProvider.ResolvePath(path, _activeSandboxId);
    }

    public async Task<SandboxInfo> CreateSandboxAsync(SandboxType type, SandboxOptions options, CancellationToken ct = default)
    {
        var provider = ResolveProvider(type);
        var effectiveOptions = new SandboxOptions
        {
            Type = type,
            SandboxRoot = options.SandboxRoot,
            RestrictNetwork = options.RestrictNetwork,
            RestrictFileSystem = options.RestrictFileSystem,
            AllowedPaths = options.AllowedPaths,
            MemoryLimitMb = options.MemoryLimitMb,
            CpuLimitPercent = options.CpuLimitPercent,
            TimeLimitSeconds = options.TimeLimitSeconds,
            DockerImage = options.DockerImage,
            EnvironmentOverrides = options.EnvironmentOverrides
        };
        return await provider.CreateSandboxAsync(effectiveOptions, ct).ConfigureAwait(false);
    }

    public async Task DestroySandboxAsync(string sandboxId, CancellationToken ct = default)
    {
        foreach (var provider in _providers.Values)
        {
            if (provider.GetSandboxInfo(sandboxId) is not null)
            {
                await provider.DestroySandboxAsync(sandboxId, ct).ConfigureAwait(false);
                return;
            }
        }

        _logger?.LogWarning("[SandboxManager] 沙箱 '{Id}' 不存在于任何 Provider 中", sandboxId);
    }

    public SandboxInfo? GetSandboxInfo(string sandboxId)
    {
        foreach (var provider in _providers.Values)
        {
            var info = provider.GetSandboxInfo(sandboxId);
            if (info is not null)
            {
                return info;
            }
        }

        return null;
    }

    public string ResolvePath(string path, string sandboxId)
    {
        foreach (var provider in _providers.Values)
        {
            if (provider.GetSandboxInfo(sandboxId) is not null)
            {
                return provider.ResolvePath(path, sandboxId);
            }
        }

        throw new InvalidOperationException($"沙箱 '{sandboxId}' 不存在");
    }

    private ISandboxProvider ResolveProvider(SandboxType type)
    {
        if (type == SandboxType.None)
        {
            var envType = Environment.GetEnvironmentVariable(JccEnvVar.SandboxMode.ToValue());
            if (!string.IsNullOrEmpty(envType))
            {
                var parsed = SandboxTypeExtensions.FromValue(envType);
                if (parsed is not null && parsed.Value != SandboxType.None)
                {
                    type = parsed.Value;
                }
            }

            if (type == SandboxType.None)
            {
                type = SandboxType.Soft;
            }
        }

        if (!_providers.TryGetValue(type, out var provider))
        {
            throw new InvalidOperationException($"沙箱类型 '{type.ToValue()}' 不可用。可用类型: {string.Join(", ", _providers.Keys.Select(k => k.ToValue()))}");
        }

        return provider;
    }

    public void Dispose() => _lock.Dispose();
}
