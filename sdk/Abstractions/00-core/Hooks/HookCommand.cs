namespace JoinCode.Abstractions.Hooks;

/// <summary>
/// 钩子命令基类
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$hook_type")]
[JsonDerivedType(typeof(BashCommandHook), HookTypeConstants.Command)]
[JsonDerivedType(typeof(PromptHook), HookTypeConstants.Prompt)]
[JsonDerivedType(typeof(AgentHook), HookTypeConstants.Agent)]
[JsonDerivedType(typeof(HttpHook), HookTypeConstants.Http)]
[JsonDerivedType(typeof(FunctionHook), HookTypeConstants.Function)]
public abstract record HookCommand
{
    /// <summary>
    /// 钩子类型
    /// </summary>
    [JsonIgnore]
    public abstract string Type { get; }

    /// <summary>
    /// 条件过滤（权限规则语法，如 "Bash(git *)"）
    /// </summary>
    [JsonPropertyName("if")]
    public string? If { get; init; }

    /// <summary>
    /// 超时时间（秒）
    /// </summary>
    public int? Timeout { get; init; }

    /// <summary>
    /// 状态消息（显示在旋转器中）
    /// </summary>
    public string? StatusMessage { get; init; }

    /// <summary>
    /// 是否只执行一次后移除
    /// </summary>
    public bool? Once { get; init; }

    /// <summary>
    /// 获取显示文本
    /// </summary>
    public virtual string GetDisplayText()
    {
        return StatusMessage ?? $"[{Type}]";
    }

    /// <summary>
    /// 检查两个钩子是否相等（比较内容和配置，不包括超时）
    /// </summary>
    public virtual bool IsEqualTo(HookCommand other)
    {
        if (Type != other.Type) return false;
        return (If ?? "") == (other.If ?? "");
    }
}

/// <summary>
/// Bash 命令钩子
/// </summary>
public sealed record BashCommandHook : HookCommand
{
    public override string Type => HookTypeConstants.Command;

    /// <summary>
    /// 要执行的命令
    /// </summary>
    public required string Command { get; init; }

    /// <summary>
    /// Shell 解释器（bash/powershell，默认 bash）
    /// </summary>
    public string? Shell { get; init; }

    /// <summary>
    /// 是否异步执行（不阻塞）
    /// </summary>
    public bool? Async { get; init; }

    /// <summary>
    /// 异步唤醒（退出码 2 时唤醒模型）
    /// </summary>
    public bool? AsyncRewake { get; init; }

    public override string GetDisplayText()
    {
        return StatusMessage ?? Command;
    }

    public override bool IsEqualTo(HookCommand other)
    {
        if (other is not BashCommandHook bash) return false;
        if (!base.IsEqualTo(other)) return false;

        return Command == bash.Command &&
               (Shell ?? "bash") == (bash.Shell ?? "bash");
    }
}

/// <summary>
/// LLM 提示钩子
/// </summary>
public sealed record PromptHook : HookCommand
{
    public override string Type => HookTypeConstants.Prompt;

    /// <summary>
    /// 提示内容（使用 $ARGUMENTS 占位符）
    /// </summary>
    public required string Prompt { get; init; }

    /// <summary>
    /// 使用的模型（如 "claude-sonnet-4-6"）
    /// </summary>
    public string? Model { get; init; }

    public override string GetDisplayText()
    {
        return StatusMessage ?? Prompt[..Math.Min(Prompt.Length, 50)];
    }

    public override bool IsEqualTo(HookCommand other)
    {
        if (other is not PromptHook prompt) return false;
        if (!base.IsEqualTo(other)) return false;

        return Prompt == prompt.Prompt;
    }
}

/// <summary>
/// 代理验证钩子
/// </summary>
public sealed record AgentHook : HookCommand
{
    public override string Type => HookTypeConstants.Agent;

    /// <summary>
    /// 验证提示（使用 $ARGUMENTS 占位符）
    /// </summary>
    public required string Prompt { get; init; }

    /// <summary>
    /// 使用的模型（默认使用小型快速模型）
    /// </summary>
    public string? Model { get; init; }

    public override string GetDisplayText()
    {
        return StatusMessage ?? $"[Agent] {Prompt[..Math.Min(Prompt.Length, 40)]}";
    }

    public override bool IsEqualTo(HookCommand other)
    {
        if (other is not AgentHook agent) return false;
        if (!base.IsEqualTo(other)) return false;

        return Prompt == agent.Prompt;
    }
}

/// <summary>
/// HTTP 钩子
/// </summary>
public sealed record HttpHook : HookCommand
{
    public override string Type => HookTypeConstants.Http;

    /// <summary>
    /// POST URL
    /// </summary>
    public required string Url { get; init; }

    /// <summary>
    /// 额外请求头
    /// </summary>
    public Dictionary<string, string>? Headers { get; init; }

    /// <summary>
    /// 允许的环境变量列表（用于请求头插值）
    /// </summary>
    public IReadOnlyList<string>? AllowedEnvVars { get; init; }

    public override string GetDisplayText()
    {
        return StatusMessage ?? Url;
    }

    public override bool IsEqualTo(HookCommand other)
    {
        if (other is not HttpHook http) return false;
        if (!base.IsEqualTo(other)) return false;

        return Url == http.Url;
    }
}

/// <summary>
/// 函数回调钩子（仅会话级，不可持久化）
/// </summary>
public sealed record FunctionHook : HookCommand
{
    public override string Type => HookTypeConstants.Function;

    /// <summary>
    /// 唯一ID（用于移除）
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// 回调函数
    /// </summary>
    [JsonIgnore]
    public required Func<HookInput, CancellationToken, Task<HookResult>> Callback { get; init; }

    /// <summary>
    /// 错误消息（回调返回 false 时显示）
    /// </summary>
    public string? ErrorMessage { get; init; }

    public override string GetDisplayText()
    {
        return StatusMessage ?? $"[Function] {Id}";
    }

    public override bool IsEqualTo(HookCommand other)
    {
        // 函数钩子无法比较（没有稳定标识符）
        return false;
    }
}

/// <summary>
/// 回调钩子（内部使用）
/// </summary>
public sealed record CallbackHook : HookCommand
{
    public override string Type => HookTypeConstants.Callback;

    /// <summary>
    /// 回调函数
    /// </summary>
    [JsonIgnore]
    public required Func<HookInput, CancellationToken, Task<HookResult>> Callback { get; init; }

    /// <summary>
    /// 是否为内部钩子（排除在指标外）
    /// </summary>
    public bool Internal { get; init; }

    public override string GetDisplayText()
    {
        return StatusMessage ?? "[Callback]";
    }

    public override bool IsEqualTo(HookCommand other)
    {
        // 回调钩子无法比较
        return false;
    }
}
