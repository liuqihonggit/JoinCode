
namespace Core.Bridge;

/// <summary>
/// Bridge 运行时门控 — 对齐 TS 端 bridgeEnabled.ts
/// 检查 Bridge 功能是否在运行时可用
/// </summary>
public static class BridgeRuntimeGate
{
    private static volatile bool _initialized;
    private static volatile bool _bridgeEnabled = true; // 默认启用
    private static volatile bool _v2BridgeEnabled;
    private static volatile bool _cseShimEnabled = true; // 默认启用
    private static volatile bool _ccrAutoConnectDefault = true;
    private static volatile bool _ccrMirrorEnabled;
    private static volatile bool _useCcrV2 = true; // v2 env-less 默认启用
    private static string? _minVersion;
    private static string? _disabledReason;
    private static readonly TaskCompletionSource<bool> _initTcs = new();

    /// <summary>初始化门控状态 — 从配置/环境变量/特性标志加载</summary>
    public static void Initialize(BridgeConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        _bridgeEnabled = config.Enabled;
        _v2BridgeEnabled = false; // v2 env-less 默认关闭
        _cseShimEnabled = true; // cse_ shim 默认启用
        _ccrAutoConnectDefault = true;
        _ccrMirrorEnabled = false;
        _minVersion = null;
        _disabledReason = config.Enabled ? null : "Bridge is disabled in configuration";

        _initialized = true;
        _initTcs.TrySetResult(_bridgeEnabled);
    }

    /// <summary>
    /// Bridge 是否启用 — 对齐 TS 端 isBridgeEnabled()
    /// 运行时检查（非阻塞）
    /// </summary>
    public static bool IsBridgeEnabled()
    {
        if (!_initialized) return false;
        return _bridgeEnabled;
    }

    /// <summary>
    /// Bridge 是否启用（阻塞式）— 对齐 TS 端 isBridgeEnabledBlocking()
    /// 等待初始化完成
    /// </summary>
    public static async Task<bool> IsBridgeEnabledBlockingAsync(CancellationToken ct = default)
    {
        if (_initialized) return _bridgeEnabled;

        using var cts = TimeoutHelper.CreateLinkedTimeout(ct, TimeSpan.FromSeconds(10));

        try
        {
            return await _initTcs.Task.WaitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// 获取 Bridge 禁用原因 — 对齐 TS 端 getBridgeDisabledReason()
    /// </summary>
    public static Task<string?> GetBridgeDisabledReasonAsync()
    {
        return Task.FromResult(_disabledReason);
    }

    /// <summary>
    /// v2 env-less 路径是否启用 — 对齐 TS 端 isEnvLessBridgeEnabled()
    /// </summary>
    public static bool IsV2BridgeEnabled()
    {
        if (!_initialized) return false;
        return _v2BridgeEnabled;
    }

    /// <summary>
    /// CCR v2 (env-less) 是否启用 — 对齐 TS 端 serverUseCcrV2 || CLAUDE_BRIDGE_USE_CCR_V2
    /// true = v2 env-less 路径; false = v1 env-based 路径
    /// </summary>
    public static bool IsCcrV2Enabled()
    {
        if (!_initialized) return true; // 默认 v2
        return _useCcrV2;
    }

    /// <summary>设置 CCR v2 开关（用于测试或特性标志）</summary>
    public static void SetCcrV2Enabled(bool enabled) => _useCcrV2 = enabled;

    /// <summary>
    /// 解析是否使用 CCR v2 — 综合 secret 标志 + 环境变量覆盖,统一 V1/V2 决策入口
    /// 对齐 TS 端: secret.use_code_sessions || CLAUDE_BRIDGE_USE_CCR_V2
    /// </summary>
    /// <param name="secretUseCodeSessions">work 密钥中的 UseCodeSessions 标志(无密钥传 null)</param>
    /// <returns>true=V2(env-less), false=V1(env-based)</returns>
    public static bool ShouldUseCcrV2(bool? secretUseCodeSessions = null)
    {
        if (secretUseCodeSessions == true) return true;
        var envOverride = Environment.GetEnvironmentVariable("CLAUDE_BRIDGE_USE_CCR_V2");
        return envOverride is "1" or "true" or "TRUE";
    }

    /// <summary>
    /// 等待网络恢复 — V1/V2 切换前统一网络检查,网络不可用时阻塞等待(带 30s 超时)
    /// 三处切换点(BridgeGate/BridgeMain/BridgeWorkPollLoop)统一调用此方法,消除重复
    /// </summary>
    public static async Task WaitForNetworkAsync(
        INetworkConnectivityService? networkService,
        ILogger? logger,
        CancellationToken ct)
    {
        if (networkService is null) return;
        if (networkService.IsNetworkAvailable()) return;

        logger?.LogWarning("Bridge V1/V2 切换:网络不可用,等待恢复...");

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<NetworkConnectivityChangedEventArgs> handler = (_, e) =>
        {
            if (e.CurrentState != NetworkConnectivityState.Offline) tcs.TrySetResult(true);
        };
        networkService.StateChanged += handler;
        try
        {
            if (!networkService.IsNetworkAvailable())
            {
                await tcs.Task.WaitAsync(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
            }
        }
        catch (TimeoutException)
        {
            logger?.LogWarning("Bridge V1/V2 切换:等待网络恢复超时(30s),继续切换");
        }
        finally
        {
            networkService.StateChanged -= handler;
        }

        logger?.LogInformation("Bridge V1/V2 切换:网络已恢复");
    }

    /// <summary>
    /// cse_ shim 是否启用 — 对齐 TS 端 isCseShimEnabled()
    /// </summary>
    public static bool IsCseShimEnabled()
    {
        if (!_initialized) return true; // 默认启用
        return _cseShimEnabled;
    }

    /// <summary>
    /// 检查 Bridge 最低版本 — 对齐 TS 端 checkBridgeMinVersion()
    /// 返回 null 表示通过，否则返回错误消息
    /// </summary>
    public static string? CheckBridgeMinVersion()
    {
        if (_minVersion is null) return null;

        // 简单版本比较 — 后续可接入实际版本号
        return null;
    }

    /// <summary>
    /// CCR 自动连接默认值 — 对齐 TS 端 getCcrAutoConnectDefault()
    /// </summary>
    public static bool GetCcrAutoConnectDefault()
    {
        return _ccrAutoConnectDefault;
    }

    /// <summary>
    /// CCR 镜像模式是否启用 — 对齐 TS 端 isCcrMirrorEnabled()
    /// </summary>
    public static bool IsCcrMirrorEnabled()
    {
        return _ccrMirrorEnabled;
    }

    /// <summary>设置 env-less 门控（用于测试或特性标志）</summary>
    public static void SetV2BridgeEnabled(bool enabled) => _v2BridgeEnabled = enabled;

    /// <summary>设置 cse_ shim 开关（用于测试或特性标志）</summary>
    public static void SetCseShimEnabled(bool enabled) => _cseShimEnabled = enabled;

    /// <summary>设置 CCR 镜像模式（用于测试或特性标志）</summary>
    public static void SetCcrMirrorEnabled(bool enabled) => _ccrMirrorEnabled = enabled;

    /// <summary>重置为未初始化状态（用于测试）</summary>
    public static void Reset()
    {
        _initialized = false;
        _bridgeEnabled = true;
        _v2BridgeEnabled = false;
        _cseShimEnabled = true;
        _ccrAutoConnectDefault = true;
        _ccrMirrorEnabled = false;
        _useCcrV2 = true;
        _minVersion = null;
        _disabledReason = null;
    }
}
