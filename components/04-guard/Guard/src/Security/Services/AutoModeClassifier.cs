namespace Core.Security.Services;

public interface IAutoModeClassifier
{
    Task<ClassificationResult> ClassifyAsync(ClassificationRequest request, CancellationToken ct = default);
}

public sealed partial class ClassificationRequest
{
    public required string ToolName { get; init; }
    public required Dictionary<string, JsonElement> Parameters { get; init; }
    public required OperationType OperationType { get; init; }
}

public sealed partial class ClassificationResult
{
    public required SecurityClassification Classification { get; init; }
    public required double Confidence { get; init; }
    public string? Reason { get; init; }
    public required SecurityAction Action { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<SecurityClassification>))]
public enum SecurityClassification { [EnumValue("safe")] Safe, [EnumValue("lowRisk")] LowRisk, [EnumValue("mediumRisk")] MediumRisk, [EnumValue("highRisk")] HighRisk, [EnumValue("dangerous")] Dangerous }

[JsonConverter(typeof(JsonStringEnumConverter<SecurityAction>))]
public enum SecurityAction { [EnumValue("autoApprove")] AutoApprove, [EnumValue("requireConfirmation")] RequireConfirmation, [EnumValue("requireApproval")] RequireApproval, [EnumValue("block")] Block }

[Register]
public sealed partial class AutoModeClassifier : IAutoModeClassifier
{
    private static readonly string[] DangerousCommandPatterns =
    [
        "rm -rf /", "rm -rf ~", "del /f /s /q c:",
        "format", "fdisk", "mkfs",
        "dd if=", ":(){ :|:& };:",
        "shutdown", "restart",
        "wmic", "reg delete",
        "net user", "net localgroup"
    ];

    private static readonly Regex[] DangerousCommandRegexes = DangerousCommandPatterns
        .Select(p => new Regex(Regex.Escape(p), RegexOptions.IgnoreCase))
        .ToArray();

    private static readonly FrozenSet<OperationType> ReadOperationTypes = FrozenSet.Create(
        OperationType.Read, OperationType.List, OperationType.Get,
        OperationType.Search, OperationType.Glob, OperationType.Grep);
    private static readonly FrozenSet<OperationType> WriteOperationTypes = FrozenSet.Create(
        OperationType.Write, OperationType.Edit, OperationType.Create, OperationType.Delete);

    [Inject] private readonly ILogger<AutoModeClassifier>? _logger;
    private readonly ITelemetryService? _telemetryService;

    public AutoModeClassifier(ILogger<AutoModeClassifier>? logger = null, ITelemetryService? telemetryService = null)
    {
        _logger = logger;
        _telemetryService = telemetryService;
    }

    public Task<ClassificationResult> ClassifyAsync(ClassificationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = ClassifyInternal(request);

        _logger?.LogDebug("[AutoModeClassifier] 工具 '{Tool}' 分类: {Classification} -> {Action} ({Reason})",
            request.ToolName, result.Classification, result.Action, result.Reason);

        _telemetryService?.RecordCount("security.classification.count", new() { ["classification"] = result.Classification.ToString(), ["action"] = result.Action.ToString() }, description: "Security classification count");

        return Task.FromResult(result);
    }

    private ClassificationResult ClassifyInternal(ClassificationRequest request)
    {
        if (ToolClassification.ReadOnlyTools.Contains(request.ToolName) || IsReadOperation(request.OperationType))
        {
            return new ClassificationResult
            {
                Classification = SecurityClassification.Safe,
                Confidence = 0.95,
                Reason = "只读操作",
                Action = SecurityAction.AutoApprove
            };
        }

        if (IsDangerousCommand(request))
        {
            return new ClassificationResult
            {
                Classification = SecurityClassification.Dangerous,
                Confidence = 0.99,
                Reason = "检测到危险命令模式",
                Action = SecurityAction.Block
            };
        }

        if (ToolClassification.SensitiveTools.Contains(request.ToolName))
        {
            return new ClassificationResult
            {
                Classification = SecurityClassification.HighRisk,
                Confidence = 0.9,
                Reason = "敏感工具操作",
                Action = SecurityAction.RequireApproval
            };
        }

        if (IsSensitivePathOperation(request))
        {
            return new ClassificationResult
            {
                Classification = SecurityClassification.MediumRisk,
                Confidence = 0.8,
                Reason = "涉及敏感路径",
                Action = SecurityAction.RequireConfirmation
            };
        }

        if (ToolClassification.SafeWriteTools.Contains(request.ToolName) || IsWriteOperation(request.OperationType))
        {
            return new ClassificationResult
            {
                Classification = SecurityClassification.LowRisk,
                Confidence = 0.85,
                Reason = "非敏感写入操作",
                Action = SecurityAction.AutoApprove
            };
        }

        return new ClassificationResult
        {
            Classification = SecurityClassification.MediumRisk,
            Confidence = 0.6,
            Reason = "未知操作类型，默认中等风险",
            Action = SecurityAction.RequireConfirmation
        };
    }

    private static bool IsReadOperation(OperationType operationType)
        => ReadOperationTypes.Contains(operationType);

    private static bool IsWriteOperation(OperationType operationType)
        => WriteOperationTypes.Contains(operationType);

    private static bool IsDangerousCommand(ClassificationRequest request)
    {
        if (!request.Parameters.TryGetValue("command", out var commandObj) || commandObj.ValueKind != JsonValueKind.String || commandObj.GetString() is not string command)
        {
            return false;
        }

        foreach (var regex in DangerousCommandRegexes)
        {
            if (regex.IsMatch(command))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSensitivePathOperation(ClassificationRequest request)
    {
        foreach (var kvp in request.Parameters)
        {
            if (kvp.Value.ValueKind == JsonValueKind.String && kvp.Value.GetString() is string strValue)
            {
                if (SecurityPatterns.IsSensitivePathSegment(strValue))
                {
                    return true;
                }
            }
        }

        return false;
    }

}
