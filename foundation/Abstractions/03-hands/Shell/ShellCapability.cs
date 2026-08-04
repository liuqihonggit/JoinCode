namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// Shell 执行器能力描述 — 长命缓存，只检测一次
/// 封装 Shell 的静态属性：类型、路径、版本、编码、DisplayName
/// 不继承 Entity（纯数据，无生命周期）
/// </summary>
public sealed class ShellCapability
{
    public ShellType Type { get; init; }
    public string ShellPath { get; init; } = "";
    public string Version { get; init; } = "unknown";
    public string DisplayName { get; init; } = "";
    public bool Detached { get; init; }
    public Encoding OutputEncoding { get; init; } = Encoding.UTF8;
    public Encoding ErrorEncoding { get; init; } = Encoding.UTF8;

    /// <summary>
    /// 是否为 PowerShell Core (7+) — 仅 PowerShell 类型有值
    /// </summary>
    public bool IsPowerShellCore { get; init; }

    /// <summary>
    /// 转为 ShellInfo 快照 — 用于提示词注入
    /// </summary>
    public ShellInfo ToShellInfo() => new()
    {
        Type = Type,
        DisplayName = DisplayName,
        ShellPath = ShellPath,
        Version = Version,
    };
}
