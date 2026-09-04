namespace JoinCode.ChatCommands;

/// <summary>
/// 前缀命令特性 — 标注 ! / !! 前缀命令处理器类。
/// 对齐 [ChatCommand] 模式，源码生成器未来可扫描此特性自动注册处理器。
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class PrefixCommandAttribute : Attribute
{
    /// <summary>前缀符号（"!" 或 "!!"）</summary>
    public required string Prefix { get; init; }

    /// <summary>命令描述</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>是否触发 AI 对话 — ! = true（输出注入上下文），!! = false（静默执行）</summary>
    public bool TriggersAi { get; init; }
}
