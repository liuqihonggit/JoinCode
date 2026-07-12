
namespace JoinCode.Abstractions.LLM.Execution;

/// <summary>
/// 规范模型标识枚举 — 用于模型名称规范化（剥离日期/提供商后缀）
/// 枚举成员按匹配优先级排列：更具体的关键字排在前面
/// 例如: claude-opus-4-6 必须在 claude-opus-4 前面，否则会被错误匹配
/// [ModelInfo] 特性由 EnumMetadataGenerator 自动按 Provider 分组生成 ModelEntry[] 数组
/// </summary>
public enum CanonicalModel
{
    // Claude 4+ 系列 — 更具体版本优先
    [EnumValue("claude-opus-4-6")]
    [ModelInfo("anthropic", "Claude Opus 4.6", 200_000, "最新 Opus，最强推理能力")]
    ClaudeOpus46,

    [EnumValue("claude-opus-4-5")]
    [ModelInfo("anthropic", "Claude Opus 4.5", 200_000, "上一代 Opus")]
    ClaudeOpus45,

    [EnumValue("claude-opus-4-1")]
    [ModelInfo("anthropic", "Claude Opus 4.1", 200_000, "Opus 4.1")]
    ClaudeOpus41,

    [EnumValue("claude-opus-4")]
    [ModelInfo("anthropic", "Claude Opus 4", 200_000, "Opus 4")]
    ClaudeOpus4,

    [EnumValue("claude-sonnet-4-6")]
    [ModelInfo("anthropic", "Claude Sonnet 4.6", 200_000, "最新 Sonnet，平衡性能与速度", IsDefault = true)]
    ClaudeSonnet46,

    [EnumValue("claude-sonnet-4-5")]
    [ModelInfo("anthropic", "Claude Sonnet 4.5", 200_000, "上一代 Sonnet")]
    ClaudeSonnet45,

    [EnumValue("claude-sonnet-4")]
    [ModelInfo("anthropic", "Claude Sonnet 4", 200_000, "Sonnet 4")]
    ClaudeSonnet4,

    [EnumValue("claude-haiku-4-5")]
    [ModelInfo("anthropic", "Claude Haiku 4.5", 200_000, "快速低成本模型", IsFastDefault = true)]
    ClaudeHaiku45,

    // Claude 3.x 系列
    [EnumValue("claude-3-7-sonnet")]
    [ModelInfo("anthropic", "Claude 3.7 Sonnet", 200_000, "3.7 Sonnet")]
    Claude37Sonnet,

    [EnumValue("claude-3-5-sonnet")]
    [ModelInfo("anthropic", "Claude 3.5 Sonnet v2", 200_000, "经典 Sonnet v2")]
    Claude35Sonnet,

    [EnumValue("claude-3-5-haiku")]
    [ModelInfo("anthropic", "Claude 3.5 Haiku", 200_000, "经典 Haiku")]
    Claude35Haiku,

    [EnumValue("claude-3-opus")]
    [ModelInfo("anthropic", "Claude 3 Opus", 200_000, "经典 Opus")]
    Claude3Opus,

    [EnumValue("claude-3-sonnet")]
    [ModelInfo("anthropic", "Claude 3 Sonnet", 200_000, "经典 Sonnet")]
    Claude3Sonnet,

    [EnumValue("claude-3-haiku")]
    [ModelInfo("anthropic", "Claude 3 Haiku", 200_000, "经典 Haiku")]
    Claude3Haiku,

    // OpenAI 系列 — 更具体版本优先
    [EnumValue("gpt-4o-mini")]
    [ModelInfo("openai", "GPT-4o Mini", 128_000, "快速低成本模型", IsFastDefault = true)]
    Gpt4oMini,

    [EnumValue("gpt-4o")]
    [ModelInfo("openai", "GPT-4o", 128_000, "旗舰多模态模型", IsDefault = true)]
    Gpt4o,

    [EnumValue("gpt-4-turbo")]
    [ModelInfo("openai", "GPT-4 Turbo", 128_000, "GPT-4 Turbo")]
    Gpt4Turbo,

    [EnumValue("gpt-4")]
    [ModelInfo("openai", "GPT-4", 128_000, "GPT-4")]
    Gpt4,

    [EnumValue("gpt-3.5-turbo")]
    [ModelInfo("openai", "GPT-3.5 Turbo", 16_385, "经典低成本模型")]
    Gpt35Turbo,

    [EnumValue("gpt-4.1-nano")]
    [ModelInfo("openai", "GPT-4.1 Nano", 1_047_576, "最快最便宜，1M 上下文")]
    Gpt41Nano,

    [EnumValue("gpt-4.1-mini")]
    [ModelInfo("openai", "GPT-4.1 Mini", 1_047_576, "高效平衡，1M 上下文")]
    Gpt41Mini,

    [EnumValue("gpt-4.1")]
    [ModelInfo("openai", "GPT-4.1", 1_047_576, "最新旗舰，1M 上下文")]
    Gpt41,

    [EnumValue("o4-mini")]
    [ModelInfo("openai", "O4 Mini", 200_000, "高效推理模型")]
    O4Mini,

    [EnumValue("o3-mini")]
    [ModelInfo("openai", "O3 Mini", 200_000, "低成本推理模型")]
    O3Mini,

    [EnumValue("o3")]
    [ModelInfo("openai", "O3", 200_000, "深度推理模型")]
    O3,

    [EnumValue("o1-mini")]
    [ModelInfo("openai", "O1 Mini", 128_000, "低成本推理模型")]
    O1Mini,

    [EnumValue("o1")]
    [ModelInfo("openai", "O1", 200_000, "推理模型")]
    O1,

    // DeepSeek
    [EnumValue("deepseek-reasoner")]
    [ModelInfo("deepseek", "DeepSeek Reasoner", 64_000, "深度推理模型")]
    DeepSeekReasoner,

    [EnumValue("deepseek-chat")]
    [ModelInfo("deepseek", "DeepSeek Chat", 64_000, "对话模型", IsDefault = true, IsFastDefault = true)]
    DeepSeekChat,

    [EnumValue("deepseek")]
    [ModelInfo("deepseek", "DeepSeek", 64_000, "通用模型")]
    DeepSeek
}
