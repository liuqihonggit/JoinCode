namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 斜杠命令元数据 — 命令清单的单条记录，供 GUI 命令面板等消费方使用。
/// 由源码生成器从 [ChatCommand] 特性自动提取，无需手动维护。
/// </summary>
public sealed record SlashCommandMetadata
{
    /// <summary>命令名（如 "/clear"）</summary>
    public required string Name { get; init; }

    /// <summary>命令描述</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>用法提示</summary>
    public string Usage { get; init; } = string.Empty;

    /// <summary>命令别名</summary>
    public string[] Aliases { get; init; } = [];

    /// <summary>是否隐藏（隐藏命令不在面板展示）</summary>
    public bool IsHidden { get; init; }

    /// <summary>是否启用（禁用命令视为无权限，从候选面板过滤）</summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>命令分类 — 由源码生成器从 [ChatCommand] 特性提取，供 slash_list 分组展示</summary>
    public string Category { get; init; } = "Other";
}

/// <summary>
/// 斜杠命令目录接口 — 由源码生成器生成的 GeneratedSlashCommandCatalog 实现，
/// 通过 DI 注入。GUI Hosting 层从此接口获取命令清单，不直接引用 CLI。
/// </summary>
public interface ISlashCommandCatalog
{
    /// <summary>按 Category 预分组的命令字典（编译时生成,O(1) 查找）;消费方按分类取列表无需每次 GroupBy。</summary>
    IReadOnlyDictionary<string, IReadOnlyList<SlashCommandMetadata>> ByCategory { get; }
}
