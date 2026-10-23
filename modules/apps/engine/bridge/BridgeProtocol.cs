namespace JoinCode.Cli.Bridge;

/// <summary>
/// bridge 请求（TUI → jcc，读 stdin）— ADR 0141 契约的 jcc 侧镜像：
/// 两端各自定义 DTO（进程协议常态），JSON 形状由 BridgeJsonContractTests 锁定防漂移。
/// newline-delimited JSON，type 判别（[JsonPolymorphic] AOT 兼容多态）。
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(BridgeRequest.Submit), "submit")]
[JsonDerivedType(typeof(BridgeRequest.Interrupt), "interrupt")]
[JsonDerivedType(typeof(BridgeRequest.SetPermissionMode), "set_permission_mode")]
[JsonDerivedType(typeof(BridgeRequest.StopTask), "stop_task")]
[JsonDerivedType(typeof(BridgeRequest.Quit), "quit")]
public abstract record BridgeRequest
{
    /// <summary>提交用户输入，开始回合。</summary>
    public sealed record Submit(string Text) : BridgeRequest;

    /// <summary>打断当前回合。</summary>
    public sealed record Interrupt() : BridgeRequest;

    /// <summary>切换权限模式（mode 取值 plan/auto/ask/bypass/unattended，与 [EnumValue] 一致）。</summary>
    public sealed record SetPermissionMode(string Mode) : BridgeRequest;

    /// <summary>停止后台任务（M8-8 后续 — kind: fork|teammate 分派 CancelFork/StopTeammate；
    /// 面板 cancelled 终态 upsert 事件回执，引擎真实终态事件随后覆盖）。</summary>
    public sealed record StopTask(string Kind, string TaskId) : BridgeRequest;

    /// <summary>退出会话。</summary>
    public sealed record Quit() : BridgeRequest;
}

/// <summary>
/// bridge 事件（jcc → TUI，写 stdout）— ADR 0141 契约的 jcc 侧镜像：
/// 会话握手（session_info）、权限模式（permission_mode_changed）、
/// 回合生命周期（turn_started/completed/interrupted）、思考（thinking_started/ended）、
/// 工具（tool_call/tool_result）、流式（token_delta）、用量汇总（token_usage）、消息（assistant_message/error）。
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(BridgeEvent.SessionInfo), "session_info")]
[JsonDerivedType(typeof(BridgeEvent.PermissionModeChanged), "permission_mode_changed")]
[JsonDerivedType(typeof(BridgeEvent.TurnStarted), "turn_started")]
[JsonDerivedType(typeof(BridgeEvent.ThinkingStarted), "thinking_started")]
[JsonDerivedType(typeof(BridgeEvent.ThinkingEnded), "thinking_ended")]
[JsonDerivedType(typeof(BridgeEvent.ToolCall), "tool_call")]
[JsonDerivedType(typeof(BridgeEvent.ToolProgress), "tool_progress")]
[JsonDerivedType(typeof(BridgeEvent.ToolResult), "tool_result")]
[JsonDerivedType(typeof(BridgeEvent.TokenDelta), "token_delta")]
[JsonDerivedType(typeof(BridgeEvent.AssistantMessage), "assistant_message")]
[JsonDerivedType(typeof(BridgeEvent.TokenUsage), "token_usage")]
[JsonDerivedType(typeof(BridgeEvent.SubagentStarted), "subagent_started")]
[JsonDerivedType(typeof(BridgeEvent.SubagentToolUse), "subagent_tool_use")]
[JsonDerivedType(typeof(BridgeEvent.SubagentFinished), "subagent_finished")]
[JsonDerivedType(typeof(BridgeEvent.ContextUsage), "context_usage")]
[JsonDerivedType(typeof(BridgeEvent.BackgroundTask), "background_task")]
[JsonDerivedType(typeof(BridgeEvent.TurnCompleted), "turn_completed")]
[JsonDerivedType(typeof(BridgeEvent.TurnInterrupted), "turn_interrupted")]
[JsonDerivedType(typeof(BridgeEvent.Error), "error")]
public abstract record BridgeEvent
{
    /// <summary>
    /// 会话握手（引擎启动后首条事件）— 版本/当前模型/权限模式/bypass 可用性/cwd。
    /// Model 为 null 表示引擎未设置模型（TUI banner 显示"当前模型未设置"）。
    /// </summary>
    public sealed record SessionInfo(string Version, string? Model, string PermissionMode, bool BypassAvailable, string Cwd) : BridgeEvent;

    /// <summary>权限模式切换回执（mode 为引擎实际生效值 — 安全闸拦截时与请求值不同）。</summary>
    public sealed record PermissionModeChanged(string Mode) : BridgeEvent;

    /// <summary>回合开始（busy 挂载 spinner）。</summary>
    public sealed record TurnStarted() : BridgeEvent;

    /// <summary>进入思考（spinner Thinking 模式）。</summary>
    public sealed record ThinkingStarted() : BridgeEvent;

    /// <summary>思考结束（durationMs 为墙钟差）。</summary>
    public sealed record ThinkingEnded(long DurationMs) : BridgeEvent;

    /// <summary>工具调用消息（bullet + bold 工具名 + args）。</summary>
    public sealed record ToolCall(string Name, string Args) : BridgeEvent;

    /// <summary>
    /// 工具执行中进度流式增量（TASK065 P3 — Bash/PowerShell 执行中实时输出，取代"卡住到结束才见结果"）：
    /// ProgressType 透传引擎侧既有类型（bash_progress/ps_progress — ShellExecutionMiddleware 每秒快照；
    /// query_update/search_results_received — WebSearch 等既有发射方零改动直达）；
    /// Message 为进度文本（bash_progress 为当前 stdout 最近行快照 — 多行文本）；
    /// TUI 侧流式项原地更新，tool_result 到达时原位替换为终态结果项 — 与 native_tui 侧逐字段镜像。
    /// </summary>
    public sealed record ToolProgress(string ToolName, string ProgressType, string Message) : BridgeEvent;

    /// <summary>
    /// diff 行种类 — 数值对齐 native_tui DiffLineKind（Context=0/Added=1/Removed=2），
    /// 与引擎 PatchLineType 同序可直转 byte；JSON 投影为数字。
    /// </summary>
    public enum BridgeDiffLineKind : byte
    {
        /// <summary>上下文行。</summary>
        Context = 0,

        /// <summary>新增行。</summary>
        Added = 1,

        /// <summary>删除行。</summary>
        Removed = 2,
    }

    /// <summary>diff 行投影（渲染子集：kind + content + 行号 TASK066 F1）— 与 native_tui DiffLine 逐字段镜像。</summary>
    public sealed record BridgeDiffLine
    {
        /// <summary>行种类（渲染前缀与着色依据）。</summary>
        public required BridgeDiffLineKind Kind { get; init; }

        /// <summary>行内容（不含 +/-/空格前缀）。</summary>
        public required string Content { get; init; }

        /// <summary>旧文件行号（1 基 — 引擎 PatchLine.OldLineNumber 直投影：Removed/Context 行有值；
        /// null 不序列化（WhenWritingNull — 旧 JSON golden 零破坏），TUI 侧降级 hunk 头解析流推进）。</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? OldLineNumber { get; init; }

        /// <summary>新文件行号（1 基 — 引擎 PatchLine.NewLineNumber 直投影：Added/Context 行有值；null 不序列化）。</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? NewLineNumber { get; init; }
    }

    /// <summary>diff hunk 投影（渲染子集：header + lines）— 与 native_tui DiffHunk 逐字节镜像。</summary>
    public sealed record BridgeDiffHunk
    {
        /// <summary>Hunk 头（如 "@@ -1,3 +1,4 @@"）。</summary>
        public required string Header { get; init; }

        /// <summary>Diff 行数组（渲染顺序）。</summary>
        public required BridgeDiffLine[] Lines { get; init; }
    }

    /// <summary>
    /// 工具结果摘要（⎿ 缩进项）+ 结构化补丁投影（M7-2 StructuredDiff）。
    /// Patch null/空不输出字段（WhenWritingNull — 旧契约 golden 不变，旧 TUI 反序列化兼容）。
    /// RawOutput 为工具原始输出多行文本（超长预截断防 64MB 原始输出撑爆 JSON 行 — 对齐基准
    /// terminal.ts renderTruncatedContent 的 PADDING 预截断语义；null 不输出字段 WhenWritingNull —
    /// 单行结果 summary 已表达不带此字段，旧 golden 零破坏）；HiddenLines 为折叠隐藏行数
    /// （0 不输出字段 WhenWritingDefault — 与 native_tui 侧 ToolResult 逐字节镜像）。
    /// </summary>
    public sealed record ToolResult(string Summary,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] BridgeDiffHunk[]? Patch = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RawOutput = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int HiddenLines = 0) : BridgeEvent;

    /// <summary>
    /// 流式 token 增量（spinner ResponseLength 累加）。M8-1 逐字流式 — 与 native_tui 侧镜像：
    /// Text 携带本次增量文本（TUI StreamingTextItem 原地追加）；null 不输出字段（WhenWritingNull —
    /// 旧 TUI 纯计数形态逐字节兼容）。
    /// </summary>
    public sealed record TokenDelta(int Length,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Text = null) : BridgeEvent;

    /// <summary>子代理启动（DSG041 批3 — 与 native_tui 侧 BridgeEvent.SubagentStarted 逐字段镜像；
    /// 同回合内全部子代理归一组，TUI 侧 SubagentGroupCoordinator 建组条目）。</summary>
    public sealed record SubagentStarted(string SubagentId, string AgentType, string Description) : BridgeEvent;

    /// <summary>子代理工具活动（DSG041 批3 — 原地更新组条目不逐条刷屏；TokenGain 为本次活动新增令牌）。</summary>
    public sealed record SubagentToolUse(string SubagentId, string ToolName, string Summary, int TokenGain) : BridgeEvent;

    /// <summary>子代理完成（DSG041 批3 — 条目定格，isError 聚合组头 ● 红；TokenTotal = 总令牌消耗
    /// 完成态权威校正 TASK062 P7 — 与 native_tui 侧逐字段镜像）。</summary>
    public sealed record SubagentFinished(string SubagentId, bool IsError, int TokenTotal) : BridgeEvent;

    /// <summary>
    /// 上下文窗口余量（M8-7 — 每回合 Done 后 TUI footer 告警显示；与 native_tui 侧镜像）。
    /// PromptTokens = 回合权威 usage.PromptTokens；PercentLeft 0-100（引擎侧阈值算法口径），
    /// 阈值标志不进协议 — UI 侧自判颜色文案。
    /// </summary>
    public sealed record ContextUsage(int PromptTokens, int ContextWindowTokens, int PercentLeft) : BridgeEvent;

    /// <summary>后台任务状态更新（M8-8 — upsert 语义：TUI 按 (kind,taskId) 更新/插入面板条目；终态由 state 自含）。</summary>
    public sealed record BackgroundTask(string Kind, string TaskId, string Description, string State) : BridgeEvent;

    /// <summary>完整助手消息（追加滚动区）。</summary>
    public sealed record AssistantMessage(string Text) : BridgeEvent;

    /// <summary>
    /// 回合令牌用量汇总（OnDone 收尾发射 — 紧随 assistant_message 之后、turn_completed 之前；
    /// TUI 侧 TokenUsageTracker 记账驱动 footer 展示）— 与 native_tui 侧 BridgeEvent.TokenUsage 逐字段镜像。
    /// InputTokens/OutputTokens 恒输出（OnDone usage 的 PromptTokens/CompletionTokens 投影）；
    /// ContextTokens/ContextLimitTokens 为上下文窗口占用/上限（null 不输出字段 WhenWritingNull —
    /// 引擎暂无 context window 数据源时省略，通道预留待接线）。
    /// </summary>
    public sealed record TokenUsage(long InputTokens, long OutputTokens,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? ContextTokens = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? ContextLimitTokens = null) : BridgeEvent;

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
    private const string SupportedRequestTypes = "submit, interrupt, set_permission_mode, stop_task, quit";

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
