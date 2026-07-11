
namespace JoinCode.Abstractions.LLM.Execution;

/// <summary>
/// 规范模型标识枚举 — 用于模型名称规范化（剥离日期/提供商后缀）
/// 枚举成员按匹配优先级排列：更具体的关键字排在前面
/// 例如: claude-opus-4-6 必须在 claude-opus-4 前面，否则会被错误匹配
/// </summary>
public enum CanonicalModel
{
    // Claude 4+ 系列 — 更具体版本优先
    [EnumValue("claude-opus-4-6")] ClaudeOpus46,
    [EnumValue("claude-opus-4-5")] ClaudeOpus45,
    [EnumValue("claude-opus-4-1")] ClaudeOpus41,
    [EnumValue("claude-opus-4")] ClaudeOpus4,
    [EnumValue("claude-sonnet-4-6")] ClaudeSonnet46,
    [EnumValue("claude-sonnet-4-5")] ClaudeSonnet45,
    [EnumValue("claude-sonnet-4")] ClaudeSonnet4,
    [EnumValue("claude-haiku-4-5")] ClaudeHaiku45,

    // Claude 3.x 系列 — 使用不同命名方案 (claude-3-{family})
    [EnumValue("claude-3-7-sonnet")] Claude37Sonnet,
    [EnumValue("claude-3-5-sonnet")] Claude35Sonnet,
    [EnumValue("claude-3-5-haiku")] Claude35Haiku,
    [EnumValue("claude-3-opus")] Claude3Opus,
    [EnumValue("claude-3-sonnet")] Claude3Sonnet,
    [EnumValue("claude-3-haiku")] Claude3Haiku,

    // OpenAI 系列 — 更具体版本优先
    [EnumValue("gpt-4o-mini")] Gpt4oMini,
    [EnumValue("gpt-4o")] Gpt4o,
    [EnumValue("gpt-4-turbo")] Gpt4Turbo,
    [EnumValue("gpt-4")] Gpt4,
    [EnumValue("gpt-3.5-turbo")] Gpt35Turbo,
    [EnumValue("gpt-4.1-nano")] Gpt41Nano,
    [EnumValue("gpt-4.1-mini")] Gpt41Mini,
    [EnumValue("gpt-4.1")] Gpt41,
    [EnumValue("o4-mini")] O4Mini,
    [EnumValue("o3-mini")] O3Mini,
    [EnumValue("o3")] O3,
    [EnumValue("o1-mini")] O1Mini,
    [EnumValue("o1")] O1,

    // DeepSeek
    [EnumValue("deepseek-reasoner")] DeepSeekReasoner,
    [EnumValue("deepseek-chat")] DeepSeekChat,
    [EnumValue("deepseek")] DeepSeek
}
