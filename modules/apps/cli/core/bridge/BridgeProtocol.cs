namespace JoinCode.Cli.Bridge;

/// <summary>
/// bridge 请求（TUI → jcc，读 stdin）— ADR 0141 契约的 jcc 侧镜像：
/// 两端各自定义 DTO（进程协议常态），JSON 形状由 BridgeJsonContractTests 锁定防漂移。
/// newline-delimited JSON，type 判别（[JsonPolymorphic] AOT 兼容多态）。
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(BridgeRequest.Submit), "submit")]
[JsonDerivedType(typeof(BridgeRequest.Interrupt), "interrupt")]
[JsonDerivedType(typeof(BridgeRequest.Quit), "quit")]
public abstract record BridgeRequest
{
    /// <summary>提交用户输入，开始回合。</summary>
    public sealed record Submit(string Text) : BridgeRequest;

    /// <summary>打断当前回合。</summary>
    public sealed record Interrupt() : BridgeRequest;

    /// <summary>退出会话。</summary>
    public sealed record Quit() : BridgeRequest;
}

/// <summary>
/// bridge 事件（jcc → TUI，写 stdout）— ADR 0141 契约的 jcc 侧镜像：
/// 回合生命周期（turn_started/completed/interrupted）、思考（thinking_started/ended）、
/// 工具（tool_call/tool_result）、流式（token_delta）、消息（assistant_message/error）。
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(BridgeEvent.TurnStarted), "turn_started")]
[JsonDerivedType(typeof(BridgeEvent.ThinkingStarted), "thinking_started")]
[JsonDerivedType(typeof(BridgeEvent.ThinkingEnded), "thinking_ended")]
[JsonDerivedType(typeof(BridgeEvent.ToolCall), "tool_call")]
[JsonDerivedType(typeof(BridgeEvent.ToolResult), "tool_result")]
[JsonDerivedType(typeof(BridgeEvent.TokenDelta), "token_delta")]
[JsonDerivedType(typeof(BridgeEvent.AssistantMessage), "assistant_message")]
[JsonDerivedType(typeof(BridgeEvent.TurnCompleted), "turn_completed")]
[JsonDerivedType(typeof(BridgeEvent.TurnInterrupted), "turn_interrupted")]
[JsonDerivedType(typeof(BridgeEvent.Error), "error")]
public abstract record BridgeEvent
{
    /// <summary>回合开始（busy 挂载 spinner）。</summary>
    public sealed record TurnStarted() : BridgeEvent;

    /// <summary>进入思考（spinner Thinking 模式）。</summary>
    public sealed record ThinkingStarted() : BridgeEvent;

    /// <summary>思考结束（durationMs 为墙钟差）。</summary>
    public sealed record ThinkingEnded(long DurationMs) : BridgeEvent;

    /// <summary>工具调用消息（bullet + bold 工具名 + args）。</summary>
    public sealed record ToolCall(string Name, string Args) : BridgeEvent;

    /// <summary>工具结果摘要（⎿ 缩进项）。</summary>
    public sealed record ToolResult(string Summary) : BridgeEvent;

    /// <summary>流式 token 增量（spinner ResponseLength 累加）。</summary>
    public sealed record TokenDelta(int Length) : BridgeEvent;

    /// <summary>完整助手消息（追加滚动区）。</summary>
    public sealed record AssistantMessage(string Text) : BridgeEvent;

    /// <summary>回合完成（idle）。</summary>
    public sealed record TurnCompleted() : BridgeEvent;

    /// <summary>回合被打断（idle）。</summary>
    public sealed record TurnInterrupted() : BridgeEvent;

    /// <summary>错误（含诱导说明）。</summary>
    public sealed record Error(string Message) : BridgeEvent;
}

/// <summary>
/// bridge 协议 JSON 上下文 — AOT 源生成（ADR 0141：DTO 双向转换全部经此上下文，禁止手写拼接）。
/// camelCase 字段命名与 native_tui 侧 BridgeJsonContext 逐字节一致；
/// Safe 上下文带 UnsafeRelaxedJsonEscaping（中文不转义，对齐 native_tui 默认输出）。
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false)]
[JsonSerializable(typeof(BridgeRequest))]
[JsonSerializable(typeof(BridgeEvent))]
public sealed partial class BridgeJsonContext : JsonSerializerContext
{
    private static readonly Lazy<BridgeJsonContext> s_safe = new(() => {
        var ctx = Default ?? throw new InvalidOperationException("JsonSerializerContext.Default is null");
        return new BridgeJsonContext(new JsonSerializerOptions(ctx.Options)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            TypeInfoResolver = ctx
        });
    });

    /// <summary>带 UnsafeRelaxedJsonEscaping 的上下文 — 不转义中文等非 ASCII 字符（协议输出口径）。</summary>
    public static BridgeJsonContext Safe => s_safe.Value;
}

/// <summary>
/// bridge 协议读写器（jcc 侧）— 事件写 stdout（一行 + LF + Flush，协议统一 \n）；
/// 请求读 stdin（空行静默跳过；未知 type / 坏 JSON 返回 Skipped + 诱导诊断）。
/// </summary>
public static class BridgeProtocol
{
    /// <summary>支持的请求 type 清单（诊断用，与 BridgeRequest 的 [JsonDerivedType] 一一对应）。</summary>
    private const string SupportedRequestTypes = "submit, interrupt, quit";

    /// <summary>读结果：成功 / 流终止 / 跳过（带诊断）。</summary>
    public enum ReadResult
    {
        /// <summary>读到一条合法请求。</summary>
        Message,

        /// <summary>流终止（无更多数据）。</summary>
        Eof,

        /// <summary>行无法识别（未知 type / 坏 JSON），已跳过 — 诊断携带诱导说明。</summary>
        Skipped,
    }

    /// <summary>序列化事件为一行 JSON。</summary>
    public static string SerializeEvent(BridgeEvent bridgeEvent) =>
        JsonSerializer.Serialize(bridgeEvent, BridgeJsonContext.Safe.BridgeEvent);

    /// <summary>
    /// 读下一条请求：空行静默跳过；未知 type / 坏 JSON 返回 Skipped（诊断含诱导说明）；
    /// 流终止返回 Eof。
    /// </summary>
    public static ReadResult TryReadRequest(TextReader reader, out BridgeRequest? request, out string? diagnostic)
    {
        while (true)
        {
            var line = reader.ReadLine();
            if (line is null)
            {
                request = null;
                diagnostic = null;
                return ReadResult.Eof;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                request = JsonSerializer.Deserialize(line, BridgeJsonContext.Safe.BridgeRequest);
                if (request is not null)
                {
                    diagnostic = null;
                    return ReadResult.Message;
                }
            }
            catch (JsonException ex)
            {
                request = null;
                diagnostic = BuildDiagnostic(line, ex);
                return ReadResult.Skipped;
            }
        }
    }

    /// <summary>构造诱导式诊断：是什么 type 触发 + 为什么跳过 + 合法 type 清单 + 接下来怎么做。</summary>
    private static string BuildDiagnostic(string line, JsonException ex)
    {
        var typeValue = TryGetTypeValue(line);
        return typeValue is null
            ? $"bridge 请求行不是合法 JSON（{ex.Message}），已跳过该行。合法 type：{SupportedRequestTypes}。请核对 TUI 输出是否符合 ADR 0141 协议。"
            : $"未知请求 type '{typeValue}'，已跳过该行（不中断请求流）。支持的 type：{SupportedRequestTypes}。若 TUI 为新版本新增请求，请同步升级 jcc 侧 BridgeRequest 契约。原始行：{line}";
    }

    /// <summary>提取行的 type 字段值（动态字段判别，仅此一处允许 JsonDocument）。</summary>
    private static string? TryGetTypeValue(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("type", out var type)
                && type.ValueKind == JsonValueKind.String
                ? type.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
