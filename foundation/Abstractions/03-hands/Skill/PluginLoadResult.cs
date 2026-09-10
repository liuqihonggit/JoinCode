namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 插件加载状态(ADR 0098) — match/switch 友好的枚举返回值
/// <para>避免 bool Success + string ErrorMessage 的模糊性</para>
/// <para>调用方可 switch (result.Status) 做不同处理</para>
/// </summary>
public enum PluginLoadStatus
{
    /// <summary>加载成功</summary>
    Success,
    /// <summary>插件已加载(重复加载)</summary>
    AlreadyLoaded,
    /// <summary>插件在黑名单中(此前卸载泄漏)</summary>
    Blacklisted,
    /// <summary>LoadAsync 返回失败</summary>
    LoadFailed,
    /// <summary>InitializeAsync 返回失败</summary>
    InitializeFailed,
    /// <summary>卸载契约校验失败</summary>
    ContractViolation,
    /// <summary>加载过程抛异常</summary>
    Exception,
}

/// <summary>
/// 插件加载结果 — 含枚举状态,match/switch 友好(ADR 0098)
/// <para>替代 bool Success + string ErrorMessage 的模糊模式</para>
/// </summary>
public sealed class PluginLoadResult
{
    /// <summary>加载状态</summary>
    public PluginLoadStatus Status { get; }
    /// <summary>插件名</summary>
    public string PluginName { get; }
    /// <summary>错误消息(Success 时为 null)</summary>
    public string? ErrorMessage { get; }
    /// <summary>是否成功</summary>
    public bool IsSuccess => Status == PluginLoadStatus.Success;

    private PluginLoadResult(PluginLoadStatus status, string pluginName, string? errorMessage = null)
    {
        Status = status;
        PluginName = pluginName;
        ErrorMessage = errorMessage;
    }

    /// <summary>加载成功</summary>
    public static PluginLoadResult Success(string pluginName) =>
        new(PluginLoadStatus.Success, pluginName);

    /// <summary>插件已加载</summary>
    public static PluginLoadResult AlreadyLoaded(string pluginName) =>
        new(PluginLoadStatus.AlreadyLoaded, pluginName, $"插件 '{pluginName}' 已经加载");

    /// <summary>插件在黑名单中</summary>
    public static PluginLoadResult Blacklisted(string pluginName) =>
        new(PluginLoadStatus.Blacklisted, pluginName, $"插件 '{pluginName}' 已被加入黑名单(此前卸载泄漏),拒绝加载");

    /// <summary>LoadAsync 失败</summary>
    public static PluginLoadResult LoadFailed(string pluginName, string error) =>
        new(PluginLoadStatus.LoadFailed, pluginName, $"插件 '{pluginName}' Load 失败: {error}");

    /// <summary>InitializeAsync 失败</summary>
    public static PluginLoadResult InitializeFailed(string pluginName, string error) =>
        new(PluginLoadStatus.InitializeFailed, pluginName, $"插件 '{pluginName}' Initialize 失败: {error}");

    /// <summary>卸载契约校验失败</summary>
    public static PluginLoadResult ContractViolation(string pluginName, string reason) =>
        new(PluginLoadStatus.ContractViolation, pluginName, $"插件 '{pluginName}' 拒绝加载: {reason}");

    /// <summary>加载异常</summary>
    public static PluginLoadResult ExceptionResult(string pluginName, string error) =>
        new(PluginLoadStatus.Exception, pluginName, $"加载异常: {error}");
}
