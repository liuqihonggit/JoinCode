using JoinCode.Abstractions.Utils;

namespace JoinCode.ChatCommands;

/// <summary>
/// 聊天命令特性 — 用于自动注册，Category 声明命令分类（特性解耦，无需中央映射表）。
/// 从 CLI 抽取到 Abstractions 共享库，CLI 与 GUI 均可引用。
/// 源码生成器扫描此特性标记的类，自动生成命令注册代码与命令元数据目录。
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ChatCommandAttribute : Attribute
{
    public required string Name { get; init; }

    public string Description { get; init; } = string.Empty;

    public string Usage { get; init; } = string.Empty;

    public string[] Aliases { get; init; } = Array.Empty<string>();

    public string ArgumentHint { get; init; } = string.Empty;

    public bool IsHidden { get; init; }

    /// <summary>
    /// 命令是否当前可用 — 对齐 TS CommandBase.isEnabled()
    /// 特性声明为静态值，动态门控需在命令类中 override IsEnabled 属性
    /// </summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>
    /// 命令分类 — 每个命令自己声明，源码生成器自动提取，无需中央映射表
    /// </summary>
    public ChatCommandCategory Category { get; init; } = ChatCommandCategory.Other;

    /// <summary>
    /// 是否暴露给 LLM/MCP — true 时斜杠命令也可被 AI 调用
    /// </summary>
    public bool ExposeToMcp { get; init; }

    /// <summary>
    /// 注入策略 — 控制命令对 AI 的可见性。默认 Slash（不注入 AI 提示词，AI 可通过 tool_search 发现）
    /// </summary>
    public ToolKind Kind { get; init; } = ToolKind.Slash;
}
