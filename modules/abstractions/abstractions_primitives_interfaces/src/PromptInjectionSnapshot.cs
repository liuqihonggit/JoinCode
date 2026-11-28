namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 每轮注入快照 — 预处理完成后从上下文管理器读取的 System 角色消息拼接
/// （静态前缀 + 动态后缀），即本轮真正进入对话请求的系统提示词。
/// </summary>
/// <param name="SystemPrompt">本轮实际注入的系统提示词全文。</param>
public sealed record PromptInjectionSnapshot(string SystemPrompt);