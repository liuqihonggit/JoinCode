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
/// 轮次闸门选项 — 收尾前必须选择下一步，编号即工具参数 choice 的取值。
/// </summary>
public enum RoundGateChoice {
    /// <summary>1 直接继续 — 不总结不规划，继续执行。</summary>
    [EnumValue("continue")] Continue = 1,

    /// <summary>2 TODO 继续 — 从任务文档取下一项执行。</summary>
    [EnumValue("next_todo")] NextTodo = 2,

    /// <summary>3 规划任务 — 先补任务清单与四列验收表。</summary>
    [EnumValue("plan")] Plan = 3,

    /// <summary>4 验收 — 真实 exe 验收，○ 标记用户亲自验收项。</summary>
    [EnumValue("acceptance")] Acceptance = 4,

    /// <summary>5 结束 — 唯一允许收尾的选项。</summary>
    [EnumValue("end")] End = 5,
}

/// <summary>
/// 轮次闸门策略 — ReAct 循环共用的"何时允许收尾"唯一裁决点。
/// <para>
/// QueryEngine（主代理路径）与 QueryLoopMiddleware（ChatService 路径）都只调用本类，
/// 禁止在任一循环内重复实现闸门判定，避免两条链路的规则漂移。
/// </para>
/// <para>
/// 闸门是 AI 自选机制：工具结果只注入对应工作流提示词，不等待用户输入，可在无人值守模式使用。
/// </para>
/// </summary>
public static class RoundGatePolicy {
    /// <summary>默认收尾拦截上限 — 防止模型死不调用闸门时无限燃烧 token。</summary>
    public const int DefaultMaxStopRejections = 6;

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
    /// 把数字选项（1~5）转为枚举。
    /// </summary>
    /// <param name="number">选项编号。</param>
    /// <param name="choice">解析结果。</param>
    /// <returns>合法返回 true。</returns>
    public static bool TryFromNumber(int number, out RoundGateChoice choice) {
        if (number is >= (int)RoundGateChoice.Continue and <= (int)RoundGateChoice.End) {
            choice = (RoundGateChoice)number;
            return true;
        }

        choice = default;
        return false;
    }

    /// <summary>
    /// 解析选项文本 — 支持编号（"1"~"5"）、枚举值（continue/next_todo/plan/acceptance/end）与少量常见别名。
    /// </summary>
    /// <param name="raw">原始值。</param>
    /// <param name="choice">解析结果。</param>
    /// <returns>合法返回 true。</returns>
    public static bool TryParseChoice(string? raw, out RoundGateChoice choice) {
        choice = default;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var value = raw.Trim().ToLowerInvariant();
        if (value.Length == 1 && TryFromNumber(value[0] - '0', out choice))
            return true;

        if (RoundGateChoiceExtensions.FromValue(value) is { } canonical) {
            choice = canonical;
            return true;
        }

        choice = value switch {
            "todo" or "next" => RoundGateChoice.NextTodo,
            "planning" => RoundGateChoice.Plan,
            "accept" or "verify" or "verification" => RoundGateChoice.Acceptance,
            "stop" or "finish" or "done" => RoundGateChoice.End,
            _ => default
        };
        return choice != default;
    }

    /// <summary>
    /// 从工具调用（已解析参数 + 原始 JSON 兜底）读取闸门选项 — QueryEngine 使用。
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
        if (!IsGateToolName(toolName))
            return false;

        if (arguments is not null && arguments.TryGetValue("choice", out var element) && TryReadElement(element, out choice))
            return true;

        return TryReadChoiceFromJson(toolName, rawArguments, out choice);
    }

    /// <summary>
    /// 从工具调用参数 JSON 读取闸门选项 — QueryLoopMiddleware 使用（ToolCallEntry.Arguments 为 JSON 字符串）。
    /// </summary>
    /// <param name="toolName">工具名。</param>
    /// <param name="argumentsJson">参数 JSON。</param>
    /// <param name="choice">解析结果。</param>
    /// <returns>是闸门调用且选项合法时返回 true。</returns>
    public static bool TryReadChoiceFromJson(string? toolName, string? argumentsJson, out RoundGateChoice choice) {
        choice = default;
        if (!IsGateToolName(toolName) || string.IsNullOrWhiteSpace(argumentsJson))
            return false;

        try {
            using var document = JsonDocument.Parse(argumentsJson);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("choice", out var element)
                && TryReadElement(element, out choice);
        } catch (JsonException) {
            return false;
        }
    }

    /// <summary>工具名是否为闸门工具（大小写不敏感）。</summary>
    /// <param name="toolName">工具名。</param>
    /// <returns>是闸门工具返回 true。</returns>
    public static bool IsGateToolName(string? toolName) =>
        string.Equals(toolName, ToolName, StringComparison.OrdinalIgnoreCase);

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
