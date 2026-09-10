namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 插件诊断类别(ADR 0098)
/// </summary>
public enum PluginDiagnosticKind
{
    /// <summary>撤销操作失败</summary>
    RevertFailed,
    /// <summary>卸载超时</summary>
    UnloadTimeout,
    /// <summary>ALC 未被 GC 回收</summary>
    AlcLeak,
    /// <summary>ALC 不可回收(isCollectible=false)</summary>
    AlcNotCollectible,
    /// <summary>插件激活失败</summary>
    ActivationFailed,
    /// <summary>插件激活成功但未登记任何副作用</summary>
    EmptyRegistration,
}

/// <summary>
/// 插件诊断事件 — 结构化诊断(ADR 0098)
/// <para>Kind 分类见 PluginDiagnosticKind,Suggestion 给出修复建议</para>
/// </summary>
public sealed class PluginDiagnostic
{
    /// <summary>插件标识</summary>
    public string PluginId { get; init; } = "";
    /// <summary>诊断类别</summary>
    public PluginDiagnosticKind Kind { get; init; }
    /// <summary>诊断消息</summary>
    public string Message { get; init; } = "";
    /// <summary>修复建议(null 表示无建议)</summary>
    public string? Suggestion { get; init; }
    /// <summary>时间戳(默认构造时 UtcNow)</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>格式化输出:[时间] [类别] [插件] 消息 + 建议</summary>
    public override string ToString() =>
        $"[{Timestamp:HH:mm:ss}] [{Kind}] [{PluginId}] {Message}" +
        (Suggestion is null ? "" : Environment.NewLine + "    建议: " + Suggestion);
}
