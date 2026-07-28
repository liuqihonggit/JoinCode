
namespace JoinCode.Abstractions.Models.Skill;

/// <summary>
/// 技能定义
/// </summary>
public sealed record SkillDefinition
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }

    [JsonPropertyName("version")]
    public string Version { get; init; } = "1.0";

    [JsonPropertyName("parameters")]
    public Dictionary<string, SkillParameter> Parameters { get; init; } = new();

    [JsonPropertyName("steps")]
    public required List<SkillStep> Steps { get; init; }

    [JsonPropertyName("requires_confirmation")]
    public bool RequiresConfirmation { get; init; } = false;

    [JsonPropertyName("timeout_seconds")]
    public int TimeoutSeconds { get; init; } = 300;

    [JsonPropertyName("author")]
    public string? Author { get; init; }

    [JsonPropertyName("tags")]
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    [JsonPropertyName("permissions")]
    public IReadOnlyList<string> Permissions { get; init; } = Array.Empty<string>();

    [JsonPropertyName("dependencies")]
    public IReadOnlyList<string> Dependencies { get; init; } = Array.Empty<string>();

    [JsonPropertyName("namespace")]
    public string? Namespace { get; init; }

    [JsonPropertyName("content_template")]
    public string? ContentTemplate { get; init; }

    [JsonPropertyName("extra")]
    public Dictionary<string, JsonElement> Extra { get; init; } = new();

    /// <summary>
    /// 技能允许的工具列表 — 对齐 TS PromptCommand.allowedTools
    /// 技能执行期间这些工具会被自动授权
    /// </summary>
    [JsonPropertyName("allowed_tools")]
    public IReadOnlyList<string> AllowedTools { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 模型覆盖 — 对齐 TS PromptCommand.model
    /// 技能执行期间切换到指定模型
    /// </summary>
    [JsonPropertyName("model")]
    public string? Model { get; init; }

    /// <summary>
    /// 推理努力级别 — 对齐 TS PromptCommand.effort
    /// 技能执行期间设置推理努力级别
    /// </summary>
    [JsonPropertyName("effort")]
    public string? Effort { get; init; }

    /// <summary>
    /// 执行模式 — 对齐 TS PromptCommand.context
    /// inline: 在当前会话内执行（支持 contextModifier）
    /// fork: 在子智能体中执行
    /// </summary>
    [JsonPropertyName("context")]
    public SkillExecutionMode Context { get; init; } = SkillExecutionMode.Inline;

    /// <summary>
    /// 禁止模型自动调用 — 对齐 TS PromptCommand.disableModelInvocation
    /// 标记为 true 时，模型不能通过 SkillTool 自动调用此技能
    /// 仅允许用户通过斜杠命令手动触发
    /// </summary>
    [JsonPropertyName("disable_model_invocation")]
    public bool DisableModelInvocation { get; init; } = false;

    /// <summary>
    /// 子智能体类型 — 对齐 TS PromptCommand.agent
    /// fork 模式下指定使用哪个 agent 类型执行（如 general-purpose, Explore, Plan）
    /// 为 null 时使用默认 agent 类型
    /// </summary>
    [JsonPropertyName("agent")]
    public string? Agent { get; init; }

    /// <summary>
    /// 隔离模式 — 对齐 TS AgentTool isolation 参数
    /// fork 模式下指定子智能体的文件系统隔离方式
    /// worktree: 在独立 git 工作树中执行，避免与主仓库冲突
    /// none: 不隔离（默认）
    /// </summary>
    [JsonPropertyName("isolation")]
    public AgentIsolationMode Isolation { get; init; } = AgentIsolationMode.None;

    [JsonIgnore]
    public string? SourcePath { get; init; }

    [JsonIgnore]
    public SkillSourceFormat SourceFormat { get; init; } = SkillSourceFormat.Json;

    [JsonIgnore]
    public DateTime LastModified { get; init; } = DateTime.UtcNow;
}

public enum SkillSourceFormat
{
    Json,
    Markdown
}

/// <summary>
/// 技能执行模式 — 对齐 TS PromptCommand.context
/// </summary>
public enum SkillExecutionMode
{
    [EnumValue("inline")] Inline,
    [EnumValue("fork")] Fork
}

public sealed class SkillParameter
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }

    [JsonPropertyName("required")]
    public bool Required { get; init; } = true;

    [JsonPropertyName("default")]
    public object? DefaultValue { get; init; }

    [JsonPropertyName("validation")]
    public ParameterValidation? Validation { get; init; }
}

public sealed class ParameterValidation
{
    [JsonPropertyName("min")]
    public double? Min { get; init; }

    [JsonPropertyName("max")]
    public double? Max { get; init; }

    [JsonPropertyName("min_length")]
    public int? MinLength { get; init; }

    [JsonPropertyName("max_length")]
    public int? MaxLength { get; init; }

    [JsonPropertyName("pattern")]
    public string? Pattern { get; init; }

    [JsonPropertyName("enum")]
    public IReadOnlyList<string>? EnumValues { get; init; }
}

public enum SkillStepType
{
    [EnumValue("tool")] Tool,
    [EnumValue("prompt")] Prompt,
    [EnumValue("condition")] Condition,
    [EnumValue("loop")] Loop,
    [EnumValue("parallel")] Parallel,
    [EnumValue("subskill")] SubSkill,
    [EnumValue("wait")] Wait
}

public sealed class SkillStep
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("type")]
    [JsonConverter(typeof(SkillStepTypeConverter))]
    public required SkillStepType Type { get; init; }

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("tool")]
    public string? Tool { get; init; }

    [JsonPropertyName("prompt")]
    public string? Prompt { get; init; }

    [JsonPropertyName("condition")]
    public string? Condition { get; init; }

    [JsonPropertyName("loop")]
    public LoopConfig? Loop { get; init; }

    [JsonPropertyName("next")]
    public string? Next { get; init; }

    [JsonPropertyName("on_error")]
    public string? OnError { get; init; }

    [JsonPropertyName("branches")]
    public Dictionary<string, List<SkillStep>>? Branches { get; init; }

    [JsonPropertyName("timeout_seconds")]
    public int? TimeoutSeconds { get; init; }

    [JsonPropertyName("retry")]
    public RetryConfig? Retry { get; init; }
}

public sealed class LoopConfig
{
    [JsonPropertyName("count")]
    public int? Count { get; init; }

    [JsonPropertyName("condition")]
    public string? Condition { get; init; }

    [JsonPropertyName("variable")]
    public string? Variable { get; init; }

    [JsonPropertyName("body")]
    public List<SkillStep>? Body { get; init; }

    [JsonPropertyName("max_iterations")]
    public int MaxIterations { get; init; } = 100;
}

public sealed class RetryConfig
{
    [JsonPropertyName("max_attempts")]
    public int MaxAttempts { get; init; } = 3;

    [JsonPropertyName("delay_ms")]
    public int DelayMs { get; init; } = 1000;

    [JsonPropertyName("exponential_backoff")]
    public bool ExponentialBackoff { get; init; } = false;
}

/// <summary>
/// SkillStepType 的 AOT 兼容 JSON 转换器 — 使用源码生成器生成的 FromValue/ToValue
/// </summary>
public sealed class SkillStepTypeConverter : JsonConverter<SkillStepType>
{
    public override SkillStepType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var stringValue = reader.GetString();
        if (stringValue is null)
            throw new JsonException("SkillStepType value cannot be null");

        var result = SkillStepTypeExtensions.FromValue(stringValue);
        if (result is null)
            throw new JsonException($"Unknown SkillStepType value: {stringValue}");

        return result.Value;
    }

    public override void Write(Utf8JsonWriter writer, SkillStepType value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToValue());
    }
}
