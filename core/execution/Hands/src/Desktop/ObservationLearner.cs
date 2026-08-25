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
