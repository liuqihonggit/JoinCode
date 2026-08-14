namespace Core.Security.Constants;

/// <summary>
/// 工具分类常量 - 统一管理工具的安全分类
/// 数据源: JoinCode.Abstractions.Security.ToolSecuritySets（由 SecurityClassGenerator 从 [SecurityClass] 特性自动生成）
/// </summary>
public static class ToolClassification
{
    /// <summary>
    /// 只读工具 - 仅读取信息，不修改任何状态
    /// </summary>
    public static readonly FrozenSet<string> ReadOnlyTools = ToolSecuritySets.ReadOnlyTools;

    /// <summary>
    /// 安全写入工具 - 在自动审批模式下被视为低风险的写入操作
    /// </summary>
    public static readonly FrozenSet<string> SafeWriteTools = ToolSecuritySets.SafeWriteTools;

    /// <summary>
    /// 敏感工具 - 需要额外确认的高风险操作（自动审批视角）
    /// </summary>
    public static readonly FrozenSet<string> SensitiveTools = ToolSecuritySets.SensitiveTools;

    /// <summary>
    /// 破坏性工具 - Agent 权限上下文中被视为破坏性的操作
    /// </summary>
    public static readonly FrozenSet<string> AgentDestructiveTools = ToolSecuritySets.AgentDestructiveTools;
}
