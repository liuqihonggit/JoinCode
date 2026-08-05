namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 系统执行器类型标识 — 替代 ShellType 枚举，支持"构造类即扩展"的注册式扩展
/// 新增执行器类型只需添加静态实例 + 对应 SystemActuatorBase 子类，无需改枚举
/// </summary>
public sealed class SystemActuatorKind
{
    /// <summary>
    /// 类型唯一标识（如 "bash", "powershell", "cmd", "python"）
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// 人类可读的显示名称（如 "Bash", "PowerShell", "CMD", "Python"）
    /// </summary>
    public string DisplayName { get; }

    private SystemActuatorKind(string id, string displayName)
    {
        Id = id;
        DisplayName = displayName;
    }

    /// <summary>
    /// Bash 执行器类型
    /// </summary>
    public static readonly SystemActuatorKind Bash = new("bash", "Bash");

    /// <summary>
    /// PowerShell 执行器类型
    /// </summary>
    public static readonly SystemActuatorKind PowerShell = new("powershell", "PowerShell");

    /// <summary>
    /// CMD 执行器类型（Windows 命令提示符）
    /// </summary>
    public static readonly SystemActuatorKind Cmd = new("cmd", "CMD");

    /// <summary>
    /// Python 执行器类型
    /// </summary>
    public static readonly SystemActuatorKind Python = new("python", "Python");

    private static readonly FrozenDictionary<string, SystemActuatorKind> _registry =
        new[] { Bash, PowerShell, Cmd, Python }.ToFrozenDictionary(
            k => k.Id, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 所有已注册的执行器类型
    /// </summary>
    public static IReadOnlyCollection<SystemActuatorKind> All => _registry.Values;

    /// <summary>
    /// 从字符串标识解析执行器类型，支持别名（pwsh→PowerShell, python3/py→Python）
    /// </summary>
    public static SystemActuatorKind? FromId(string? id)
    {
        if (id is null) return null;
        if (_registry.TryGetValue(id, out var kind)) return kind;

        return id.Equals("pwsh", StringComparison.OrdinalIgnoreCase)
            ? PowerShell
            : id.Equals("python3", StringComparison.OrdinalIgnoreCase) || id.Equals("py", StringComparison.OrdinalIgnoreCase)
            ? Python
            : null;
    }

    /// <summary>
    /// 尝试从字符串标识解析执行器类型
    /// </summary>
    public static bool TryFromId(string? id, [NotNullWhen(true)] out SystemActuatorKind? kind)
    {
        kind = FromId(id);
        return kind is not null;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
        => obj is SystemActuatorKind other && string.Equals(Id, other.Id, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public override int GetHashCode()
        => StringComparer.OrdinalIgnoreCase.GetHashCode(Id);

    /// <summary>
    /// 相等比较
    /// </summary>
    public static bool operator ==(SystemActuatorKind? left, SystemActuatorKind? right)
        => left is null ? right is null : left.Equals(right);

    /// <summary>
    /// 不等比较
    /// </summary>
    public static bool operator !=(SystemActuatorKind? left, SystemActuatorKind? right)
        => !(left == right);

    /// <inheritdoc />
    public override string ToString() => Id;
}
