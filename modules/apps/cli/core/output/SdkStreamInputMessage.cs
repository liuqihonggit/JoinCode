namespace JoinCode.Cli.Output;

/// <summary>
/// SDK 流式输入消息（TASK138，<c>--input-format stream-json</c>）— 客户端逐行写 stdin 的用户消息。
/// <para>只支持 <c>type:"user"</c>：<c>message.content</c> 可以是字符串，也可以是内容块数组
/// （仅取 <c>text</c> 块）。其余 type（如 control_request / interrupt）未实现，
/// 由 <see cref="SdkStreamInputParser"/> 给出诊断后跳过 —— 见 TASK138 §未做。</para>
/// </summary>
public sealed record SdkStreamInputMessage {
    /// <summary>消息类型（user 是唯一被消费的类型）</summary>
    [System.Text.Json.Serialization.JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    /// <summary>消息体（role + content）</summary>
    [System.Text.Json.Serialization.JsonPropertyName("message")]
    public SdkStreamInputBody? Message { get; init; }
}

/// <summary>SDK 流式输入消息体 — 对齐基准侧 user message 形状。</summary>
public sealed record SdkStreamInputBody {
    /// <summary>消息角色（user/assistant，仅 user 被消费）</summary>
    [System.Text.Json.Serialization.JsonPropertyName("role")]
    public string? Role { get; init; }

    /// <summary>内容 — 字符串或内容块数组（JSON 原样保留，由解析器按 Kind 分派）</summary>
    [System.Text.Json.Serialization.JsonPropertyName("content")]
    public System.Text.Json.JsonElement? Content { get; init; }
}
