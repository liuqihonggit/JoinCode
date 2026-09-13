
namespace Core.Configuration.Remote;

/// <summary>
/// 托管设置项 — 描述一个由远程服务管理的键值对设置
/// </summary>
public sealed class ManagedSetting
{
    /// <summary>设置键名</summary>
    public required string Key { get; init; }
    /// <summary>设置值</summary>
    public required string Value { get; init; }
    /// <summary>设置作用域</summary>
    public required SettingScope Scope { get; init; }
    /// <summary>是否只读</summary>
    public required bool IsReadOnly { get; init; }
    /// <summary>设置描述，可为空</summary>
    public string? Description { get; init; }
    /// <summary>最后更新时间，可为空</summary>
    public DateTime? UpdatedAt { get; init; }
    /// <summary>最后更新者，可为空</summary>
    public string? UpdatedBy { get; init; }
}

/// <summary>
/// 设置作用域枚举 — 标识设置生效的层级范围
/// </summary>
public enum SettingScope
{
    /// <summary>用户级设置 — 仅对当前用户生效</summary>
    [EnumValue("user")] User,
    /// <summary>团队级设置 — 对整个团队生效</summary>
    [EnumValue("team")] Team,
    /// <summary>组织级设置 — 对整个组织生效</summary>
    [EnumValue("organization")] Organization,
    /// <summary>系统级设置 — 全局最高优先级</summary>
    [EnumValue("system")] System
}
