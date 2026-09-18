namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 插件类型 — 区分三种插件 Host (Workflow/External/Native)
/// <para>用于 PluginManager 统一字典中按类型分发卸载/查询逻辑</para>
/// </summary>
public enum PluginKind
{
    /// <summary>工作流插件 (编译时已知,AOT兼容)</summary>
    [EnumValue("workflow")] Workflow,

    /// <summary>外部进程插件 (stdio 通信)</summary>
    [EnumValue("external")] External,

    /// <summary>Native DLL 插件 (P/Invoke)</summary>
    [EnumValue("native")] Native,
}
