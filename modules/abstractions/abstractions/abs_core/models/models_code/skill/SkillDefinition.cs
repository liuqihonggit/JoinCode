
namespace JoinCode.Abstractions.Models.Skill;

/// <summary>
/// 技能定义
/// </summary>
public sealed record SkillDefinition {
    /// <summary>获取技能名称。</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>获取技能描述。</summary>
    [JsonPropertyName("description")]
    public required string Description { get; init; }

    /// <summary>获取技能版本。</summary>
    [JsonPropertyName("version")]
    public string Version { get; init; } = "1.0";

    /// <summary>获取技能参数字典。</summary>
    [JsonPropertyName("parameters")]
    public Dictionary<string, SkillParameter> Parameters { get; init; } = new();

    /// <summary>获取技能执行步骤列表。</summary>
    [JsonPropertyName("steps")]
    public required List<SkillStep> Steps { get; init; }

    /// <summary>获取是否需要用户确认。</summary>
    [JsonPropertyName("requires_confirmation")]
    public bool RequiresConfirmation { get; init; } = false;

    /// <summary>获取技能超时时间(秒)。</summary>
    [JsonPropertyName("timeout_seconds")]
    public int TimeoutSeconds { get; init; } = 300;

    /// <summary>获取技能作者。</summary>
    [JsonPropertyName("author")]
    public string? Author { get; init; }

    /// <summary>获取技能标签列表。</summary>
    [JsonPropertyName("tags")]
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    /// <summary>获取技能所需权限列表。</summary>
    [JsonPropertyName("permissions")]
    public IReadOnlyList<string> Permissions { get; init; } = Array.Empty<string>();

    /// <summary>获取技能依赖列表。</summary>
    [JsonPropertyName("dependencies")]
    public IReadOnlyList<string> Dependencies { get; init; } = Array.Empty<string>();

    /// <summary>获取技能命名空间。</summary>
    [JsonPropertyName("namespace")]
    public string? Namespace { get; init; }

    /// <summary>获取内容模板。</summary>
    [JsonPropertyName("content_template")]
    public string? ContentTemplate { get; init; }

    /// <summary>获取扩展数据字典。</summary>
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

    /// <summary>获取技能源文件路径。</summary>
    [JsonIgnore]
    public string? SourcePath { get; init; }

    /// <summary>获取技能源格式。</summary>
    [JsonIgnore]
    public SkillSourceFormat SourceFormat { get; init; } = SkillSourceFormat.Json;

    /// <summary>获取最后修改时间。</summary>
    [JsonIgnore]
    public DateTime LastModified { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 构建步骤 Id 索引，供 while 循环 O(1) 查找，避免每次 FirstOrDefault 线性扫描
    /// </summary>
    public Dictionary<string, SkillStep> BuildStepIndex() {
        var dict = new Dictionary<string, SkillStep>(Steps.Count, StringComparer.Ordinal);
        foreach (var s in Steps) {
            if (!dict.ContainsKey(s.Id)) {
                dict[s.Id] = s;
            }
        }
        return dict;
    }
}

public enum SkillSourceFormat {
    [EnumValue("json")] Json,
    [EnumValue("markdown")] Markdown
}

/// <summary>
/// 技能执行模式 — 对齐 TS PromptCommand.context
/// </summary>
public enum SkillExecutionMode {
    [EnumValue("inline")] Inline,
    [EnumValue("fork")] Fork
}

public sealed class SkillParameter {
    /// <summary>获取参数类型。</summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>获取参数描述。</summary>
    [JsonPropertyName("description")]
    public required string Description { get; init; }

    /// <summary>获取是否必填。</summary>
    [JsonPropertyName("required")]
    public bool Required { get; init; } = true;

    /// <summary>获取参数默认值。</summary>
    [JsonPropertyName("default")]
    public object? DefaultValue { get; init; }

    /// <summary>获取参数验证规则。</summary>
    [JsonPropertyName("validation")]
    public ParameterValidation? Validation { get; init; }
}

public sealed class ParameterValidation {
    /// <summary>获取最小值。</summary>
    [JsonPropertyName("min")]
    public double? Min { get; init; }

    /// <summary>获取最大值。</summary>
    [JsonPropertyName("max")]
    public double? Max { get; init; }

    /// <summary>获取最小长度。</summary>
    [JsonPropertyName("min_length")]
    public int? MinLength { get; init; }

    /// <summary>获取最大长度。</summary>
    [JsonPropertyName("max_length")]
    public int? MaxLength { get; init; }

    /// <summary>获取正则模式。</summary>
    [JsonPropertyName("pattern")]
    public string? Pattern { get; init; }

    /// <summary>获取枚举值列表。</summary>
    [JsonPropertyName("enum")]
    public IReadOnlyList<string> EnumValues { get; init; } = [];
}

public enum SkillStepType {
    [EnumValue("tool")] Tool,
    [EnumValue("prompt")] Prompt,
    [EnumValue("condition")] Condition,
    [EnumValue("loop")] Loop,
    [EnumValue("parallel")] Parallel,
    [EnumValue("subskill")] SubSkill,
    [EnumValue("wait")] Wait
}

public sealed class SkillStep {
    /// <summary>获取步骤标识。</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>获取步骤类型。</summary>
    [JsonPropertyName("type")]
    [JsonConverter(typeof(SkillStepTypeConverter))]
    public required SkillStepType Type { get; init; }

    /// <summary>获取步骤描述。</summary>
    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    /// <summary>获取工具名称。</summary>
    [JsonPropertyName("tool")]
    public string? Tool { get; init; }

    /// <summary>获取提示文本。</summary>
    [JsonPropertyName("prompt")]
    public string? Prompt { get; init; }

    /// <summary>获取条件表达式。</summary>
    [JsonPropertyName("condition")]
    public string? Condition { get; init; }

    /// <summary>获取循环配置。</summary>
    [JsonPropertyName("loop")]
    public LoopConfig? Loop { get; init; }

    /// <summary>获取下一步步骤标识。</summary>
    [JsonPropertyName("next")]
    public string? Next { get; init; }

    /// <summary>获取错误处理步骤标识。</summary>
    [JsonPropertyName("on_error")]
    public string? OnError { get; init; }

    /// <summary>获取分支步骤字典。</summary>
    [JsonPropertyName("branches")]
    public Dictionary<string, List<SkillStep>> Branches { get; init; } = [];

    /// <summary>获取步骤超时时间(秒)。</summary>
    [JsonPropertyName("timeout_seconds")]
    public int? TimeoutSeconds { get; init; }

    /// <summary>获取重试配置。</summary>
    [JsonPropertyName("retry")]
    public RetryConfig? Retry { get; init; }
}

public sealed class LoopConfig {
    /// <summary>获取循环次数。</summary>
    [JsonPropertyName("count")]
    public int? Count { get; init; }

    /// <summary>获取循环条件表达式。</summary>
    [JsonPropertyName("condition")]
    public string? Condition { get; init; }

    /// <summary>获取循环变量名。</summary>
    [JsonPropertyName("variable")]
    public string? Variable { get; init; }

    /// <summary>获取循环体步骤列表。</summary>
    [JsonPropertyName("body")]
    public List<SkillStep> Body { get; init; } = [];

    /// <summary>获取最大迭代次数。</summary>
    [JsonPropertyName("max_iterations")]
    public int MaxIterations { get; init; } = 100;
}

public sealed class RetryConfig {
    /// <summary>获取最大重试次数。</summary>
    [JsonPropertyName("max_attempts")]
    public int MaxAttempts { get; init; } = 3;

    /// <summary>获取重试延迟(毫秒)。</summary>
    [JsonPropertyName("delay_ms")]
    public int DelayMs { get; init; } = 1000;

    /// <summary>获取是否启用指数退避。</summary>
    [JsonPropertyName("exponential_backoff")]
    public bool ExponentialBackoff { get; init; } = false;
}

/// <summary>
/// SkillStepType 的 AOT 兼容 JSON 转换器 — 使用源码生成器生成的 FromValue/ToValue
/// </summary>
public sealed class SkillStepTypeConverter : JsonConverter<SkillStepType> {
    public override SkillStepType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        var stringValue = reader.GetString();
        if (stringValue is null)
            throw new JsonException("SkillStepType value cannot be null");

        var result = SkillStepTypeExtensions.FromValue(stringValue);
        if (result is null)
            throw new JsonException($"Unknown SkillStepType value: {stringValue}");

        return result.Value;
    }

    public override void Write(Utf8JsonWriter writer, SkillStepType value, JsonSerializerOptions options) {
        writer.WriteStringValue(value.ToValue());
    }
}