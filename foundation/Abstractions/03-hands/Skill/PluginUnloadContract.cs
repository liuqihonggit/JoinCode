namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 插件卸载契约校验结果 — 加载时由 PluginManager 调用 ValidateUnloadContract 获取
/// <para>对齐方案B:不写对应卸载就不允许加载</para>
/// <para>IsValid=false 时 PluginManager 拒绝加载,抛 [INF-PLUGIN-CONTRACT]</para>
/// </summary>
public sealed record PluginUnloadContract(
    bool IsValid,
    string? Reason,
    IReadOnlyList<string> Violations)
{
    /// <summary>契约通过 — 无违规项</summary>
    public static readonly PluginUnloadContract Valid = new(true, null, []);

    /// <summary>契约失败 — 拼接违规项作为 Reason</summary>
    public static PluginUnloadContract Invalid(params string[] violations) =>
        new(false, string.Join("; ", violations), violations);
}

/// <summary>
/// 非空撤销委托 — 构造时校验非 null,保证"有加载必有撤销"
/// <para>对齐 Cordis ctx.effect(disposer):disposer 不能为空</para>
/// <para>用于方案C PluginContext 包装 LoadFromPlugin 返回的 Action</para>
/// <para>方案B 阶段先定义类型,方案C 阶段开始强制使用</para>
/// </summary>
public readonly struct NonEmptyUndo
{
    private readonly Action _undo;

    /// <summary>构造非空撤销委托 — undo 为 null 抛 ArgumentNullException</summary>
    public NonEmptyUndo(Action undo)
    {
        ArgumentNullException.ThrowIfNull(undo);
        _undo = undo;
    }

    /// <summary>执行撤销</summary>
    public void Invoke() => _undo();

    /// <summary>隐式转 Action — 兼容现有 _pluginUndoChain(List&lt;Action&gt;)</summary>
    public static implicit operator Action(NonEmptyUndo x) => x._undo;
}
