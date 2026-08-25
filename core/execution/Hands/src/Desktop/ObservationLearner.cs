namespace JoinCode.Hands.Desktop;

/// <summary>
/// 观察学习器 — 用多模态 LLM 从用户演示中学习操作模式并优化（PRD L-02/L-04）
/// </summary>
[Register(typeof(IObservationLearner), ServiceLifetime.Singleton)]
public sealed partial class ObservationLearner : ServiceEntity, IObservationLearner
{
    private readonly IQueryService _queryService;
    private readonly ILogger<ObservationLearner>? _logger;

    private static readonly ChatOptions LearningChatOptions = new()
    {
        Temperature = 0.4f,
        MaxTokens = 4000
    };

    public ObservationLearner(IQueryService queryService, ILogger<ObservationLearner>? logger = null)
    {
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
        _logger = logger;
    }

    /// <summary>操作抽象（L-02）— 将原始操作序列抽象为参数化逻辑</summary>
    public async Task<AbstractOperationLogic> AbstractAsync(ObservedSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();

        var operationsDescription = BuildOperationsDescription(session);
        var prompt = new StringBuilder(512)
            .AppendLine("你是一个操作模式分析专家。分析以下用户操作序列，抽象出参数化的操作逻辑。")
            .AppendLine()
            .AppendLine($"操作序列（{session.Operations.Count} 步）:")
            .AppendLine(operationsDescription)
            .AppendLine()
            .AppendLine("返回 JSON 格式（只返回 JSON）:")
            .AppendLine("{")
            .AppendLine("  \"name\": \"<操作模式名称>\",")
            .AppendLine("  \"pattern\": \"<操作模式描述>\",")
            .AppendLine("  \"parameters\": \"<参数化描述,如target={应用},text={内容}>\",")
            .AppendLine("  \"steps\": [\"<抽象步骤1>\", \"<抽象步骤2>\", ...],")
            .AppendLine("  \"confidence\": <0.0到1.0>")
            .AppendLine("}")
            .ToString();

        var messages = new MessageList();
        messages.AddSystemMessage(prompt);
        messages.AddUserMessage("请分析并抽象这组操作序列。");

        var responseList = await _queryService.GetApiMessageContentsAsync(messages, LearningChatOptions, cancellationToken: cancellationToken).ConfigureAwait(false);
        var responseText = responseList.FirstOrDefault()?.Content ?? string.Empty;

        _logger?.LogDebug("操作抽象响应长度: {Length}", responseText.Length);
        return ParseAbstractLogic(responseText, session.Name);
    }

    /// <summary>观察复现（L-03）— 从抽象逻辑 + 上下文生成可执行操作序列（Macro）</summary>
    public async Task<Macro> ReproduceAsync(AbstractOperationLogic logic, string context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(logic);
        cancellationToken.ThrowIfCancellationRequested();

        var stepsDesc = string.Join(" → ", logic.Steps);
        var prompt = new StringBuilder(512)
            .AppendLine("你是一个操作复现专家。根据抽象操作逻辑和目标上下文,生成具体的桌面操作序列。")
            .AppendLine()
            .AppendLine("抽象操作逻辑:")
            .AppendLine($"  名称: {logic.Name}")
            .AppendLine($"  模式: {logic.Pattern}")
            .AppendLine($"  参数: {logic.Parameters}")
            .AppendLine($"  步骤: {stepsDesc}")
            .AppendLine($"  置信度: {logic.Confidence:F2}")
            .AppendLine()
            .AppendLine($"目标上下文: {context}")
            .AppendLine()
            .AppendLine("返回 JSON 格式（只返回 JSON）:")
            .AppendLine("{")
            .AppendLine("  \"operations\": [")
            .AppendLine("    {\"kind\": \"Click|TypeText|KeyPress|Move|Drag\", \"x\": 0, \"y\": 0, \"text\": null, \"mouseAction\": null, \"modifiers\": null, \"succeeded\": true}")
            .AppendLine("  ]")
            .AppendLine("}")
            .AppendLine("kind 可选值: Move, Click, Drag, KeyPress, TypeText, WindowFocus, WindowMove, WindowClose, Screenshot")
            .ToString();

        var messages = new MessageList();
        messages.AddSystemMessage(prompt);
        messages.AddUserMessage("请生成具体的操作序列。");

        var responseList = await _queryService.GetApiMessageContentsAsync(messages, LearningChatOptions, cancellationToken: cancellationToken).ConfigureAwait(false);
        var responseText = responseList.FirstOrDefault()?.Content ?? string.Empty;

        _logger?.LogDebug("操作复现响应长度: {Length}", responseText.Length);
        return ParseMacroFromResponse(responseText, logic.Name);
    }

    internal static Macro ParseMacroFromResponse(string responseText, string macroName)
    {
        var operations = ParseOperations(responseText);
        return new Macro(macroName, operations, DateTimeOffset.UtcNow);
    }

    internal static IReadOnlyList<DesktopOperation> ParseOperations(string responseText)
    {
        var json = ExtractJson(responseText);
        if (string.IsNullOrEmpty(json))
            return [];

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("operations", out var opsProp) || opsProp.ValueKind != JsonValueKind.Array)
                return [];

            var result = new List<DesktopOperation>();
            foreach (var op in opsProp.EnumerateArray())
            {
                var parsed = ParseSingleOperation(op);
                if (parsed is not null)
                    result.Add(parsed);
            }
            return result;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    internal static DesktopOperation? ParseSingleOperation(JsonElement element)
    {
        if (!element.TryGetProperty("kind", out var kindProp))
            return null;
        var kindStr = kindProp.GetString();
        if (string.IsNullOrEmpty(kindStr) || !Enum.TryParse<DesktopOperationKind>(kindStr, ignoreCase: true, out var kind))
            return null;

        var x = element.TryGetProperty("x", out var xProp) && xProp.TryGetInt32(out var xv) ? xv : 0;
        var y = element.TryGetProperty("y", out var yProp) && yProp.TryGetInt32(out var yv) ? yv : 0;
        var text = element.TryGetProperty("text", out var tProp) ? tProp.GetString() : null;
        var succeeded = !element.TryGetProperty("succeeded", out var sProp) || sProp.GetBoolean();

        MouseAction? mouseAction = null;
        if (element.TryGetProperty("mouseAction", out var maProp))
        {
            var maStr = maProp.GetString();
            if (!string.IsNullOrEmpty(maStr) && Enum.TryParse<MouseAction>(maStr, ignoreCase: true, out var ma))
                mouseAction = ma;
        }

        KeyModifier? modifiers = null;
        if (element.TryGetProperty("modifiers", out var modProp))
        {
            var modStr = modProp.GetString();
            if (!string.IsNullOrEmpty(modStr) && Enum.TryParse<KeyModifier>(modStr, ignoreCase: true, out var mod))
                modifiers = mod;
        }

        return new DesktopOperation(kind, x, y, text, mouseAction, modifiers, DateTimeOffset.UtcNow, succeeded, null);
    }

    /// <summary>步骤优化（L-04）— 分析抽象逻辑并提出优化建议</summary>
    public async Task<string> OptimizeAsync(AbstractOperationLogic logic, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(logic);
        cancellationToken.ThrowIfCancellationRequested();

        var prompt = $"""
            你是一个操作优化专家。分析以下抽象操作逻辑，提出优化建议。

            操作模式: {logic.Name}
            描述: {logic.Pattern}
            参数: {logic.Parameters}
            步骤: {string.Join(" → ", logic.Steps)}
            置信度: {logic.Confidence:F2}

            请分析:
            1. 是否有冗余步骤可以合并？
            2. 是否有等待时间可以缩短？
            3. 是否有更高效的替代操作？
            4. 是否有错误恢复策略缺失？

            返回优化建议（纯文本，不要 JSON）:
            """;

        var messages = new MessageList();
        messages.AddSystemMessage(prompt);
        messages.AddUserMessage("请提出优化建议。");

        var responseList = await _queryService.GetApiMessageContentsAsync(messages, LearningChatOptions, cancellationToken: cancellationToken).ConfigureAwait(false);
        var responseText = responseList.FirstOrDefault()?.Content ?? string.Empty;

        _logger?.LogDebug("步骤优化响应长度: {Length}", responseText.Length);
        return string.IsNullOrEmpty(responseText) ? "无法生成优化建议" : responseText;
    }

    internal static string BuildOperationsDescription(ObservedSession session)
    {
        var sb = new StringBuilder(256);
        for (var i = 0; i < session.Operations.Count; i++)
        {
            var op = session.Operations[i];
            sb.AppendLine($"  [{i + 1}] {op.Kind} @ ({op.X},{op.Y})" +
                (op.Text is not null ? $" text=\"{op.Text}\"" : string.Empty) +
                (op.MouseAction is not null ? $" action={op.MouseAction}" : string.Empty) +
                (op.Modifiers is not null ? $" mods={op.Modifiers}" : string.Empty) +
                $" {(op.Succeeded ? "✓" : "✗")}");
        }
        return sb.ToString();
    }

    internal static AbstractOperationLogic ParseAbstractLogic(string responseText, string fallbackName)
    {
        var json = ExtractJson(responseText);
        if (string.IsNullOrEmpty(json))
            return new AbstractOperationLogic(fallbackName, "无法抽象", string.Empty, [], 0.0);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var name = root.TryGetProperty("name", out var nProp) ? nProp.GetString() ?? fallbackName : fallbackName;
            var pattern = root.TryGetProperty("pattern", out var pProp) ? pProp.GetString() ?? string.Empty : string.Empty;
            var parameters = root.TryGetProperty("parameters", out var paramProp) ? paramProp.GetString() ?? string.Empty : string.Empty;
            var confidence = root.TryGetProperty("confidence", out var cProp) && cProp.TryGetDouble(out var cv) ? cv : 0.5;

            var steps = new List<string>();
            if (root.TryGetProperty("steps", out var stepsProp) && stepsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in stepsProp.EnumerateArray())
                {
                    var step = s.GetString();
                    if (!string.IsNullOrWhiteSpace(step))
                        steps.Add(step);
                }
            }

            return new AbstractOperationLogic(name, pattern, parameters, steps, confidence);
        }
        catch (JsonException)
        {
            return new AbstractOperationLogic(fallbackName, "解析失败", string.Empty, [], 0.0);
        }
    }

    internal static string ExtractJson(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
            return string.Empty;

        var trimmed = responseText.Trim();

        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline >= 0)
            {
                var inner = trimmed.AsSpan(firstNewline + 1);
                var endFence = inner.LastIndexOf("```");
                if (endFence >= 0)
                    inner = inner[..endFence];
                return inner.ToString().Trim();
            }
        }

        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start >= 0 && end > start)
            return trimmed.Substring(start, end - start + 1);

        return trimmed;
    }
}
