namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 轮次闸门模式 — JCC_ROUND_GATE 的三态语义（on/off/auto）。
/// </summary>
public enum RoundGateMode {
    /// <summary>完全关闭 — 不注入提示、不拦截收尾。</summary>
    [EnumValue("off")] Off,

    /// <summary>强制开启 — 主代理收尾必须过闸门（子代理除外）。</summary>
    [EnumValue("on")] On,

    /// <summary>自动 — 仅主代理交互式会话启用（headless/ci/sdk profile 或 stdin 重定向时关闭）。</summary>
    [EnumValue("auto")] Auto,
}

/// <summary>
/// 闸门选项类别 — 决定该选项在状态机中的语义（执行 / 验证 / 元操作 / 收敛 / 挂起 / 出口）。
/// </summary>
public enum RoundGateCategory {
    /// <summary>执行 — 继续推进产出。</summary>
    Execute,

    /// <summary>验证 — 对已有产出做验收。</summary>
    Verify,

    /// <summary>元操作 — 往回走：重规划 / 回退修正。</summary>
    Meta,

    /// <summary>收敛 — 做不完时的合法出口（降级 / 求助）。</summary>
    Converge,

    /// <summary>挂起 — 等后台信号。</summary>
    Suspend,

    /// <summary>出口 — 允许停止本轮。</summary>
    Exit,
}

/// <summary>
/// 自验结论 — choice=3 时必须声明自验是否通过。
/// </summary>
public enum RoundGateVerdict {
    /// <summary>未声明（视为未验证）。</summary>
    [EnumValue("none")] None,

    /// <summary>通过 — 需求逐条满足。</summary>
    [EnumValue("pass")] Pass,

    /// <summary>不通过 — 存在不满足项，需回退修正。</summary>
    [EnumValue("fail")] Fail,
}

/// <summary>
/// 轮次闸门选项 — 收尾前必须选择下一步，编号即工具参数 choice 的取值。
/// <para>
/// 四类出口（设计：执行 / 验证 / 元操作 / 收敛）+ 挂起 + 出口：
/// 执行 1/2、验证 3/4、元操作 7/8、收敛 9/10、挂起 6、出口 5（隐式出口 0）。
/// </para>
/// </summary>
public enum RoundGateChoice {
    /// <summary>0 阻塞上报（隐式出口，无编号）— 缺用户输入才能继续，直接停下等用户。</summary>
    [EnumValue("blocked")] BlockedReport = 0,

    /// <summary>1 直接继续 — 有明确下一步动作，直接执行。</summary>
    [EnumValue("continue")] Continue = 1,

    /// <summary>2 TODO 继续 — 有未关闭清单项，取下一项执行。</summary>
    [EnumValue("next_todo")] NextTodo = 2,

    /// <summary>3 自验 — 逐条对照原始需求做自我阶段性验收。</summary>
    [EnumValue("self_accept")] SelfAccept = 3,

    /// <summary>4 用户验收 — 高风险/歧义/主观项移交用户验收（○ 清单）。</summary>
    [EnumValue("user_accept")] UserAccept = 4,

    /// <summary>5 完成 — 终验通过后的完成出口（唯一"交付"出口）。</summary>
    [EnumValue("done")] Done = 5,

    /// <summary>6 等候后台 — 有真实后台句柄，挂起等信号。</summary>
    [EnumValue("await_background")] AwaitBackground = 6,

    /// <summary>7 重新规划 — 路线错/卡死/原计划失效，重新出计划。</summary>
    [EnumValue("replan")] Replan = 7,

    /// <summary>8 回退/修正 — 上轮产出有缺陷，定点返工。</summary>
    [EnumValue("rework")] Rework = 8,

    /// <summary>9 降级交付 — 无法全做完，交付可用子集（需先移交用户验收）。</summary>
    [EnumValue("degrade")] Degrade = 9,

    /// <summary>10 求助/升级 — 缺工具/权限/信息，模型自己解决不了。</summary>
    [EnumValue("escalate")] Escalate = 10,
}

/// <summary>
/// 轮次闸门策略 — ReAct 循环共用的"何时允许收尾"唯一裁决点。
/// <para>
/// QueryEngine（主代理路径）与子代理/工作流都只调用本类 + <see cref="RoundGateSession"/>，
/// 禁止在任一循环内重复实现闸门判定，避免两条链路的规则漂移。
/// </para>
/// <para>
/// 闸门是 AI 自选机制：工具结果只注入对应工作流提示词，不等待用户输入，可在无人值守模式使用。
/// </para>
/// </summary>
public static class RoundGatePolicy {
    /// <summary>默认收尾拦截上限 — 防止模型死不调用闸门时无限燃烧 token。</summary>
    public const int DefaultMaxStopRejections = 6;

    /// <summary>最小合法选项编号（隐式出口 阻塞上报）。</summary>
    public const int MinChoiceNumber = (int)RoundGateChoice.BlockedReport;

    /// <summary>最大合法选项编号（求助/升级）。</summary>
    public const int MaxChoiceNumber = (int)RoundGateChoice.Escalate;

    /// <summary>闸门工具名（来自 SystemToolName 枚举唯一数据源）。</summary>
    public static string ToolName => SystemToolNameEnumConstants.RoundGate;

    /// <summary>
    /// 读取闸门模式 — 未设置/非法值均回退 auto（开关三态：on/off/auto，兼容 1/0/true/false）。
    /// </summary>
    /// <returns>解析后的闸门模式。</returns>
    public static RoundGateMode ReadMode() {
        var raw = Environment.GetEnvironmentVariable(JccEnvVar.RoundGate.ToValue());
        if (string.IsNullOrWhiteSpace(raw))
            return RoundGateMode.Auto;

        var value = raw.Trim().ToLowerInvariant();
        if (value is "1" or "true" or "yes")
            return RoundGateMode.On;
        if (value is "0" or "false" or "no")
            return RoundGateMode.Off;

        return RoundGateModeExtensions.FromValue(value) ?? RoundGateMode.Auto;
    }

    /// <summary>
    /// 读取收尾拦截上限 — JCC_ROUND_GATE_MAX_REJECT，≤0 表示不限制，未设置回退默认值。
    /// </summary>
    /// <returns>拦截上限。</returns>
    public static int ReadMaxStopRejections() {
        var raw = Environment.GetEnvironmentVariable(JccEnvVar.RoundGateMaxReject.ToValue());
        return int.TryParse(raw, out var parsed) && parsed >= 0 ? parsed : DefaultMaxStopRejections;
    }

    /// <summary>
    /// 读取严格模式开关 — JCC_ROUND_GATE_STRICT=1 时「完成/降级」必须已移交用户验收。
    /// </summary>
    /// <returns>严格模式返回 true。</returns>
    public static bool ReadStrictMode() {
        var raw = Environment.GetEnvironmentVariable(JccEnvVar.RoundGateStrict.ToValue());
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        return raw.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on";
    }

    /// <summary>
    /// 主代理是否需要过闸门 — auto 模式按运行宿主判定：非 base profile 或 stdin 被重定向时视为无人值守，关闭闸门。
    /// </summary>
    /// <returns>需要强制闸门返回 true。</returns>
    public static bool IsEnabledForMainAgent() => ReadMode() switch {
        RoundGateMode.Off => false,
        RoundGateMode.On => true,
        _ => IsInteractiveHost()
    };

    /// <summary>
    /// ChatService 路径（GUI 回退/slash 命令）是否启用闸门 — 该路径只在显式 on 时启用，
    /// 避免短问答命令被强制多跑一轮。
    /// </summary>
    /// <returns>显式 on 返回 true。</returns>
    public static bool IsEnabledForChatPipeline() => ReadMode() == RoundGateMode.On;

    /// <summary>
    /// 是否允许再次拦截收尾 — maxRejections ≤0 表示不限制。
    /// </summary>
    /// <param name="rejections">已拦截次数。</param>
    /// <param name="maxRejections">拦截上限。</param>
    /// <returns>允许继续拦截返回 true。</returns>
    public static bool CanRejectStop(int rejections, int maxRejections) =>
        maxRejections <= 0 || rejections < maxRejections;

    /// <summary>
    /// 选项类别 — 决定该选项属于执行/验证/元操作/收敛/挂起/出口。
    /// </summary>
    /// <param name="choice">闸门选项。</param>
    /// <returns>选项类别。</returns>
    public static RoundGateCategory CategoryOf(RoundGateChoice choice) => choice switch {
        RoundGateChoice.Continue or RoundGateChoice.NextTodo => RoundGateCategory.Execute,
        RoundGateChoice.SelfAccept or RoundGateChoice.UserAccept => RoundGateCategory.Verify,
        RoundGateChoice.Replan or RoundGateChoice.Rework => RoundGateCategory.Meta,
        RoundGateChoice.Degrade or RoundGateChoice.Escalate => RoundGateCategory.Converge,
        RoundGateChoice.AwaitBackground => RoundGateCategory.Suspend,
        _ => RoundGateCategory.Exit
    };

    /// <summary>
    /// 是否为"可收尾选项" — 只有这些选项才可能允许本轮停止（是否真允许由 <see cref="RoundGateSession"/> 终验裁决）。
    /// <para>
    /// 与 <see cref="CategoryOf"/> 分离：类别表达状态机语义（如 9/10 属收敛、4 属验证），
    /// 而收敛/验证里的 4/9/10 同样需要把本轮交给用户，故单独判定收尾能力。
    /// </para>
    /// </summary>
    /// <param name="choice">闸门选项。</param>
    /// <returns>可收尾选项返回 true。</returns>
    public static bool IsExitChoice(RoundGateChoice choice) => choice switch {
        RoundGateChoice.UserAccept
            or RoundGateChoice.Done
            or RoundGateChoice.AwaitBackground
            or RoundGateChoice.Degrade
            or RoundGateChoice.Escalate
            or RoundGateChoice.BlockedReport => true,
        _ => false
    };

    /// <summary>
    /// 选项短标签 — 阻断消息与推荐提示的唯一数据源。
    /// </summary>
    /// <param name="choice">闸门选项。</param>
    /// <returns>形如"3 自验"的短标签。</returns>
    public static string Describe(RoundGateChoice choice) => choice switch {
        RoundGateChoice.BlockedReport => "0 阻塞上报（隐式出口）",
        RoundGateChoice.Continue => "1 直接继续",
        RoundGateChoice.NextTodo => "2 TODO 继续",
        RoundGateChoice.SelfAccept => "3 自验",
        RoundGateChoice.UserAccept => "4 用户验收",
        RoundGateChoice.Done => "5 完成",
        RoundGateChoice.AwaitBackground => "6 等候后台",
        RoundGateChoice.Replan => "7 重新规划",
        RoundGateChoice.Rework => "8 回退/修正",
        RoundGateChoice.Degrade => "9 降级交付",
        RoundGateChoice.Escalate => "10 求助/升级",
        _ => $"未知选项 {(int)choice}"
    };

    /// <summary>
    /// 把数字选项（0~10）转为枚举。
    /// </summary>
    /// <param name="number">选项编号。</param>
    /// <param name="choice">解析结果。</param>
    /// <returns>合法返回 true。</returns>
    public static bool TryFromNumber(int number, out RoundGateChoice choice) {
        if (number is >= MinChoiceNumber and <= MaxChoiceNumber
            && Enum.IsDefined(typeof(RoundGateChoice), number)) {
            choice = (RoundGateChoice)number;
            return true;
        }

        choice = default;
        return false;
    }

    /// <summary>
    /// 解析选项文本 — 支持编号（"0"~"10"）、枚举值（continue/self_accept/done…）与常见别名。
    /// </summary>
    /// <param name="raw">原始值。</param>
    /// <param name="choice">解析结果。</param>
    /// <returns>合法返回 true。</returns>
    public static bool TryParseChoice(string? raw, out RoundGateChoice choice) {
        choice = default;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var value = raw.Trim().ToLowerInvariant();
        if (value.All(char.IsAsciiDigit) && int.TryParse(value, out var number))
            return TryFromNumber(number, out choice);

        if (RoundGateChoiceExtensions.FromValue(value) is { } canonical) {
            choice = canonical;
            return true;
        }

        RoundGateChoice? alias = value switch {
            "todo" or "next" => RoundGateChoice.NextTodo,
            "plan" or "planning" => RoundGateChoice.Replan,
            "verify" or "verification" or "self_check" or "accept" => RoundGateChoice.SelfAccept,
            "acceptance" or "user_verify" or "user_acceptance" => RoundGateChoice.UserAccept,
            "end" or "stop" or "finish" or "complete" => RoundGateChoice.Done,
            "wait" or "await" or "background" => RoundGateChoice.AwaitBackground,
            "rollback" or "fix" or "correct" => RoundGateChoice.Rework,
            "fallback" or "partial" => RoundGateChoice.Degrade,
            "help" or "ask" or "upgrade" => RoundGateChoice.Escalate,
            "block" or "blocked_report" or "阻塞" => RoundGateChoice.BlockedReport,
            _ => null
        };

        if (alias is not { } resolved)
            return false;

        choice = resolved;
        return true;
    }

    /// <summary>
    /// 解析自验结论 — 支持 pass/fail/通过/不通过 等写法；空值视为未声明。
    /// </summary>
    /// <param name="raw">原始值。</param>
    /// <param name="verdict">解析结果。</param>
    /// <returns>空值或合法值返回 true；未知值返回 false。</returns>
    public static bool TryParseVerdict(string? raw, out RoundGateVerdict verdict) {
        if (string.IsNullOrWhiteSpace(raw)) {
            verdict = RoundGateVerdict.None;
            return true;
        }

        var value = raw.Trim().ToLowerInvariant();
        if (RoundGateVerdictExtensions.FromValue(value) is { } canonical) {
            verdict = canonical;
            return true;
        }

        verdict = value switch {
            "通过" or "绿" or "成功" or "ok" or "success" or "green" => RoundGateVerdict.Pass,
            "不通过" or "红" or "失败" or "未通过" or "fail" or "failed" or "red" => RoundGateVerdict.Fail,
            _ => RoundGateVerdict.None
        };
        return verdict != RoundGateVerdict.None || string.Equals(value, "none", StringComparison.Ordinal);
    }

    /// <summary>
    /// 从工具调用（已解析参数 + 原始 JSON 兜底）读取闸门选项 — 保留给只关心选项的调用方。
    /// </summary>
    /// <param name="toolName">工具名。</param>
    /// <param name="arguments">已解析参数。</param>
    /// <param name="rawArguments">原始参数 JSON（参数解析失败时兜底）。</param>
    /// <param name="choice">解析结果。</param>
    /// <returns>是闸门调用且选项合法时返回 true。</returns>
    public static bool TryReadChoice(
        string? toolName,
        IReadOnlyDictionary<string, JsonElement>? arguments,
        string? rawArguments,
        out RoundGateChoice choice) {
        choice = default;
        if (!TryReadInvocation(toolName, arguments, rawArguments, out var invocation))
            return false;

        choice = invocation.Choice;
        return true;
    }

    /// <summary>
    /// 从工具调用参数 JSON 读取闸门选项 — 仅选项场景（历史/事件回放）。
    /// </summary>
    /// <param name="toolName">工具名。</param>
    /// <param name="argumentsJson">参数 JSON。</param>
    /// <param name="choice">解析结果。</param>
    /// <returns>是闸门调用且选项合法时返回 true。</returns>
    public static bool TryReadChoiceFromJson(string? toolName, string? argumentsJson, out RoundGateChoice choice) {
        choice = default;
        if (!TryReadInvocation(toolName, arguments: null, argumentsJson, out var invocation))
            return false;

        choice = invocation.Choice;
        return true;
    }

    /// <summary>
    /// 读取完整闸门调用 — 选项 + 证据 + 自验结论 + 缺陷标识。
    /// </summary>
    /// <param name="toolName">工具名。</param>
    /// <param name="arguments">已解析参数。</param>
    /// <param name="rawArguments">原始参数 JSON（参数解析失败时兜底）。</param>
    /// <param name="invocation">解析结果。</param>
    /// <returns>是闸门调用且选项合法时返回 true。</returns>
    public static bool TryReadInvocation(
        string? toolName,
        IReadOnlyDictionary<string, JsonElement>? arguments,
        string? rawArguments,
        out RoundGateInvocation invocation) {
        invocation = RoundGateInvocation.Empty;
        if (!IsGateToolName(toolName))
            return false;

        if (arguments is not null && arguments.TryGetValue("choice", out var choiceElement)) {
            if (!TryReadElement(choiceElement, out var choice))
                return false;

            invocation = BuildInvocation(choice,
                ReadString(arguments, "note"),
                ReadString(arguments, "evidence"),
                ReadString(arguments, "subject"),
                ReadString(arguments, "verdict"));
            return true;
        }

        return TryReadInvocationFromJson(toolName, rawArguments, out invocation);
    }

    /// <summary>
    /// 工具名是否为闸门工具（大小写不敏感）。
    /// </summary>
    /// <param name="toolName">工具名。</param>
    /// <returns>是闸门工具返回 true。</returns>
    public static bool IsGateToolName(string? toolName) =>
        string.Equals(toolName, ToolName, StringComparison.OrdinalIgnoreCase);

    private static bool TryReadInvocationFromJson(string? toolName, string? argumentsJson, out RoundGateInvocation invocation) {
        invocation = RoundGateInvocation.Empty;
        if (!IsGateToolName(toolName) || string.IsNullOrWhiteSpace(argumentsJson))
            return false;

        try {
            using var document = JsonDocument.Parse(argumentsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return false;

            var root = document.RootElement;
            if (!root.TryGetProperty("choice", out var choiceElement) || !TryReadElement(choiceElement, out var choice))
                return false;

            invocation = BuildInvocation(choice,
                GetStringProperty(root, "note"),
                GetStringProperty(root, "evidence"),
                GetStringProperty(root, "subject"),
                GetStringProperty(root, "verdict"));
            return true;
        } catch (JsonException) {
            return false;
        }
    }

    private static RoundGateInvocation BuildInvocation(
        RoundGateChoice choice, string? note, string? evidence, string? subject, string? verdictRaw) {
        TryParseVerdict(verdictRaw, out var verdict);
        return new RoundGateInvocation {
            Choice = choice,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            Evidence = string.IsNullOrWhiteSpace(evidence) ? null : evidence.Trim(),
            Subject = string.IsNullOrWhiteSpace(subject) ? null : subject.Trim(),
            Verdict = verdict,
        };
    }

    private static string? ReadString(IReadOnlyDictionary<string, JsonElement> arguments, string name) =>
        arguments.TryGetValue(name, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;

    private static string? GetStringProperty(JsonElement root, string name) =>
        root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;

    private static bool TryReadElement(JsonElement element, out RoundGateChoice choice) => element.ValueKind switch {
        JsonValueKind.Number when element.TryGetInt32(out var number) => TryFromNumber(number, out choice),
        JsonValueKind.String => TryParseChoice(element.GetString(), out choice),
        _ => Fail(out choice)
    };

    private static bool Fail(out RoundGateChoice choice) {
        choice = default;
        return false;
    }

    /// <summary>auto 模式的宿主判定：非 base profile 或 stdin 重定向 → 无人值守。</summary>
    private static bool IsInteractiveHost() {
        var profile = Environment.GetEnvironmentVariable(JccEnvVar.NodeProfile.ToValue());
        if (!string.IsNullOrWhiteSpace(profile)
            && !string.Equals(profile, "base", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        return !Console.IsInputRedirected;
    }
}

/// <summary>
/// 一次闸门调用的完整语义 — 选项 + 说明 + 可验证证据 + 缺陷标识 + 自验结论。
/// </summary>
public sealed record RoundGateInvocation {
    /// <summary>空调用（解析失败时的占位值）。</summary>
    public static RoundGateInvocation Empty { get; } = new();

    /// <summary>闸门选项。</summary>
    public RoundGateChoice Choice { get; init; } = RoundGateChoice.BlockedReport;

    /// <summary>一句话说明下一步动作（也作为申诉理由）。</summary>
    public string? Note { get; init; }

    /// <summary>本轮可验证证据（命令输出/文件/测试结果）。</summary>
    public string? Evidence { get; init; }

    /// <summary>缺陷/子项标识 — 用于同一缺陷返工计数。</summary>
    public string? Subject { get; init; }

    /// <summary>自验结论（choice=3 时必填）。</summary>
    public RoundGateVerdict Verdict { get; init; } = RoundGateVerdict.None;
}
