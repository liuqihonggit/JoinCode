namespace Core.Security.Services;

/// <summary>
/// LLM 增强的自动模式分类器 — 两阶段分类
/// Stage 1: 规则快速分类（委托 AutoModeClassifier）
/// Stage 2: LLM 深度分类（仅对复杂命令或低置信度结果）
/// 对齐 TS yoloClassifier.ts 的两阶段架构
/// </summary>
[Register(typeof(ILlmAutoModeClassifier), ServiceLifetime.Singleton)]
public sealed partial class LlmAutoModeClassifier : ServiceEntity, ILlmAutoModeClassifier
{
    private readonly IAutoModeClassifier _ruleClassifier;
    private readonly IQueryEngine? _queryEngine;
    private readonly ILogger<LlmAutoModeClassifier>? _logger;
    private readonly ITelemetryService? _telemetryService;
    private readonly double _llmThreshold;

    private static readonly FrozenSet<char> CommandSeparators = FrozenSet.Create('&', '|', ';');

    /// <summary>
    /// 创建 LlmAutoModeClassifier
    /// </summary>
    /// <param name="ruleClassifier">规则分类器（Stage 1）</param>
    /// <param name="queryEngine">查询引擎（Stage 2，null 则不启用 LLM 分类）</param>
    /// <param name="logger">日志器</param>
    /// <param name="telemetryService">遥测服务</param>
    /// <param name="llmThreshold">LLM 分类触发阈值（置信度低于此值时启用 LLM，默认 0.8）</param>
    public LlmAutoModeClassifier(
        IAutoModeClassifier ruleClassifier,
        IQueryEngine? queryEngine = null,
        ILogger<LlmAutoModeClassifier>? logger = null,
        ITelemetryService? telemetryService = null,
        double llmThreshold = 0.8)
    {
        _ruleClassifier = ruleClassifier;
        _queryEngine = queryEngine;
        _logger = logger;
        _telemetryService = telemetryService;
        _llmThreshold = llmThreshold;
    }

    /// <inheritdoc />
    public async Task<ClassificationResult> ClassifyAsync(ClassificationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stage1 = await _ruleClassifier.ClassifyAsync(request, ct).ConfigureAwait(false);

        if (!ShouldUseLlm(stage1, request))
        {
            return stage1;
        }

        if (_queryEngine is null)
        {
            return stage1;
        }

        var stage2 = await ClassifyWithLlmAsync(request, stage1, ct).ConfigureAwait(false);
        return stage2 ?? stage1;
    }

    /// <summary>
    /// 判断是否需要 LLM 分类 — 低置信度或复杂命令
    /// </summary>
    private bool ShouldUseLlm(ClassificationResult stage1, ClassificationRequest request)
    {
        if (stage1.Confidence < _llmThreshold)
        {
            return true;
        }

        if (IsComplexCommand(request))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// 检测复杂命令 — 包含命令分隔符（&amp;&amp;、||、;）或管道链
    /// </summary>
    private static bool IsComplexCommand(ClassificationRequest request)
    {
        if (!request.Parameters.TryGetValue("command", out var commandObj) ||
            commandObj.ValueKind != JsonValueKind.String ||
            commandObj.GetString() is not string command)
        {
            return false;
        }

        foreach (var sep in CommandSeparators)
        {
            if (command.Contains(sep))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// LLM 深度分类 — 发送侧查询让 LLM 判断命令安全性
    /// </summary>
    private async Task<ClassificationResult?> ClassifyWithLlmAsync(
        ClassificationRequest request,
        ClassificationResult stage1,
        CancellationToken ct)
    {
        var command = GetCommandString(request);
        if (string.IsNullOrEmpty(command))
        {
            return null;
        }

        var prompt = BuildClassificationPrompt(command, request.ToolName);

        try
        {
            var response = await _queryEngine!.ExecuteQueryAsync(prompt, ct).ConfigureAwait(false);
            var result = ParseLlmResponse(response, stage1);

            _logger?.LogDebug("[LlmAutoModeClassifier] LLM 分类: {Classification} -> {Action} ({Reason})",
                result.Classification, result.Action, result.Reason);

            _telemetryService?.RecordCount("security.llm_classification.count", new()
            {
                ["classification"] = result.Classification.ToString(),
                ["action"] = result.Action.ToString(),
                ["stage1_classification"] = stage1.Classification.ToString()
            }, description: "LLM security classification count");

            return result;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[LlmAutoModeClassifier] LLM 分类失败，回退到 Stage 1 结果");
            _telemetryService?.RecordCount("security.llm_classification.error", description: "LLM classification error count");
            return null;
        }
    }

    private static string GetCommandString(ClassificationRequest request)
    {
        if (request.Parameters.TryGetValue("command", out var cmd) && cmd.ValueKind == JsonValueKind.String)
        {
            return cmd.GetString() ?? string.Empty;
        }
        return string.Empty;
    }

    private static string BuildClassificationPrompt(string command, string toolName)
    {
        return "分析以下命令的安全性，返回 JSON 格式结果。\n\n" +
               $"工具: {toolName}\n" +
               $"命令: {command}\n\n" +
               "请评估：\n" +
               "1. 是否包含危险操作（删除、格式化、提权）\n" +
               "2. 是否有副作用（修改文件、安装软件、网络请求）\n" +
               "3. 命令组合的风险（管道、条件执行、子命令）\n" +
               "4. 影响范围（本地文件、系统配置、网络）\n\n" +
               "返回 JSON:\n" +
               "{\"classification\": \"safe|lowRisk|mediumRisk|highRisk|dangerous\", \"confidence\": 0.0-1.0, \"reason\": \"简短说明\", \"action\": \"autoApprove|requireConfirmation|requireApproval|block\"}";
    }

    private static ClassificationResult ParseLlmResponse(string response, ClassificationResult fallback)
    {
        var json = ExtractJson(response);
        if (string.IsNullOrEmpty(json))
        {
            return fallback;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var classification = root.TryGetProperty("classification", out var classElem)
                ? ParseClassification(classElem.GetString() ?? "mediumRisk")
                : fallback.Classification;

            var confidence = root.TryGetProperty("confidence", out var confElem)
                ? confElem.GetDouble()
                : fallback.Confidence;

            var reason = root.TryGetProperty("reason", out var reasonElem)
                ? reasonElem.GetString()
                : "LLM 分类";

            var action = root.TryGetProperty("action", out var actionElem)
                ? ParseAction(actionElem.GetString() ?? "requireConfirmation")
                : GetDefaultAction(classification);

            return new ClassificationResult
            {
                Classification = classification,
                Confidence = confidence,
                Reason = reason,
                Action = action
            };
        }
        catch
        {
            return fallback;
        }
    }

    private static string? ExtractJson(string response)
    {
        var start = response.IndexOf('{');
        var end = response.LastIndexOf('}');
        if (start < 0 || end < 0 || end <= start)
        {
            return null;
        }
        return response.Substring(start, end - start + 1);
    }

    private static SecurityClassification ParseClassification(string value)
        => value.ToLowerInvariant() switch
        {
            "safe" => SecurityClassification.Safe,
            "lowrisk" => SecurityClassification.LowRisk,
            "mediumrisk" => SecurityClassification.MediumRisk,
            "highrisk" => SecurityClassification.HighRisk,
            "dangerous" => SecurityClassification.Dangerous,
            _ => SecurityClassification.MediumRisk
        };

    private static SecurityAction ParseAction(string value)
        => value.ToLowerInvariant() switch
        {
            "autoapprove" => SecurityAction.AutoApprove,
            "requireconfirmation" => SecurityAction.RequireConfirmation,
            "requireapproval" => SecurityAction.RequireApproval,
            "block" => SecurityAction.Block,
            _ => SecurityAction.RequireConfirmation
        };

    private static SecurityAction GetDefaultAction(SecurityClassification classification)
        => classification switch
        {
            SecurityClassification.Safe => SecurityAction.AutoApprove,
            SecurityClassification.LowRisk => SecurityAction.AutoApprove,
            SecurityClassification.MediumRisk => SecurityAction.RequireConfirmation,
            SecurityClassification.HighRisk => SecurityAction.RequireApproval,
            SecurityClassification.Dangerous => SecurityAction.Block,
            _ => SecurityAction.RequireConfirmation
        };
}

/// <summary>
/// LLM 增强的自动模式分类器接口
/// </summary>
public interface ILlmAutoModeClassifier
{
    /// <summary>分类命令安全性（两阶段：规则 + LLM）</summary>
    Task<ClassificationResult> ClassifyAsync(ClassificationRequest request, CancellationToken ct = default);
}
