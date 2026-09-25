namespace Core.Security.Services;

/// <summary>
/// 自动模式分类器接口 — 对工具调用进行安全分类,决定自动批准/需确认/需审批/阻断
/// </summary>
public interface IAutoModeClassifier {
    /// <summary>
    /// 对工具调用请求进行安全分类
    /// </summary>
    Task<ClassificationResult> ClassifyAsync(ClassificationRequest request, CancellationToken ct = default);
}

/// <summary>
/// 分类请求 — 描述待分类的工具调用
/// </summary>
public sealed partial class ClassificationRequest {
    /// <summary>工具名称</summary>
    public required string ToolName { get; init; }
    /// <summary>工具调用参数</summary>
    public required Dictionary<string, JsonElement> Parameters { get; init; }
    /// <summary>操作类型</summary>
    public required OperationType OperationType { get; init; }
}

/// <summary>
/// 分类结果 — 包含安全级别、置信度、原因与建议动作
/// </summary>
public sealed partial class ClassificationResult {
    /// <summary>安全分类级别</summary>
    public required SecurityClassification Classification { get; init; }
    /// <summary>置信度(0.0-1.0)</summary>
    public required double Confidence { get; init; }
    /// <summary>分类原因</summary>
    public string? Reason { get; init; }
    /// <summary>建议的安全动作</summary>
    public required SecurityAction Action { get; init; }
}

/// <summary>
/// 安全分类级别 — 从安全到危险递增
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<SecurityClassification>))]
public enum SecurityClassification {
    /// <summary>安全 — 无风险操作</summary>
    [EnumValue("safe")] Safe,
    /// <summary>低风险 — 可自动批准</summary>
    [EnumValue("lowRisk")] LowRisk,
    /// <summary>中风险 — 需确认</summary>
    [EnumValue("mediumRisk")] MediumRisk,
    /// <summary>高风险 — 需审批</summary>
    [EnumValue("highRisk")] HighRisk,
    /// <summary>危险 — 直接阻止</summary>
    [EnumValue("dangerous")] Dangerous
}

/// <summary>
/// 安全动作 — 分类器建议的处置方式
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<SecurityAction>))]
public enum SecurityAction {
    /// <summary>自动批准 — 无需用户介入</summary>
    [EnumValue("autoApprove")] AutoApprove,
    /// <summary>需要确认 — 运行前提示用户确认</summary>
    [EnumValue("requireConfirmation")] RequireConfirmation,
    /// <summary>需要审批 — 提交审批流程</summary>
    [EnumValue("requireApproval")] RequireApproval,
    /// <summary>阻止 — 拒绝执行</summary>
    [EnumValue("block")] Block
}

/// <summary>
/// 自动模式分类器实现 — 基于工具名、操作类型、危险命令模式与敏感路径的综合判定
/// </summary>
[Register(typeof(IAutoModeClassifier), ServiceLifetime.Singleton)]
public sealed partial class AutoModeClassifier : ServiceEntity, IAutoModeClassifier {
    // 委托 DangerousCommandCatalog.DangerousCommandPatterns（唯一数据源）— P0-② 单数据源改造
    // P2-⑨ 源+派生缓存合并: 直接从唯一数据源构建 Regex[],消除中间 string[] 字段
    private static readonly Regex[] DangerousCommandRegexes = DangerousCommandCatalog.DangerousCommandPatterns
        .Select(p => new Regex(Regex.Escape(p), RegexOptions.IgnoreCase))
        .ToArray();

    /// <summary>
    /// 只读操作位掩码 — 替代 FrozenSet&lt;OperationType&gt;，O(1) 位运算无哈希查找。
    /// Read=0, List=5, Get=6, Search=7, Glob=8, Grep=9
    /// </summary>
    private static readonly int ReadOperationMask = BitMask.Of(
        OperationType.Read, OperationType.List, OperationType.Get,
        OperationType.Search, OperationType.Glob, OperationType.Grep);

    /// <summary>
    /// 写入操作位掩码 — 替代 FrozenSet&lt;OperationType&gt;，O(1) 位运算无哈希查找。
    /// Write=1, Edit=2, Create=3, Delete=4
    /// </summary>
    private static readonly int WriteOperationMask = BitMask.Of(
        OperationType.Write, OperationType.Edit, OperationType.Create, OperationType.Delete);

    private readonly ILogger<AutoModeClassifier>? _logger;
    private readonly ITelemetryService? _telemetryService;

    /// <summary>
    /// 构造自动模式分类器
    /// </summary>
    public AutoModeClassifier(ILogger<AutoModeClassifier>? logger = null, ITelemetryService? telemetryService = null) {
        _logger = logger;
        _telemetryService = telemetryService;
    }

    /// <inheritdoc />
    public Task<ClassificationResult> ClassifyAsync(ClassificationRequest request, CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(request);

        var result = ClassifyInternal(request);

        _logger?.LogDebug("[AutoModeClassifier] 工具 '{Tool}' 分类: {Classification} -> {Action} ({Reason})",
            request.ToolName, result.Classification, result.Action, result.Reason);

        _telemetryService?.RecordCount("security.classification.count", new() { ["classification"] = result.Classification.ToString(), ["action"] = result.Action.ToString() }, description: "Security classification count");

        return Task.FromResult(result);
    }

    private ClassificationResult ClassifyInternal(ClassificationRequest request) {
        if (ToolClassification.ReadOnlyTools.Contains(request.ToolName) || IsReadOperation(request.OperationType)) {
            return new ClassificationResult {
                Classification = SecurityClassification.Safe,
                Confidence = 0.95,
                Reason = "只读操作",
                Action = SecurityAction.AutoApprove
            };
        }

        if (IsDangerousCommand(request)) {
            return new ClassificationResult {
                Classification = SecurityClassification.Dangerous,
                Confidence = 0.99,
                Reason = "检测到危险命令模式",
                Action = SecurityAction.Block
            };
        }

        if (ToolClassification.SensitiveTools.Contains(request.ToolName)) {
            return new ClassificationResult {
                Classification = SecurityClassification.HighRisk,
                Confidence = 0.9,
                Reason = "敏感工具操作",
                Action = SecurityAction.RequireApproval
            };
        }

        if (IsSensitivePathOperation(request)) {
            return new ClassificationResult {
                Classification = SecurityClassification.MediumRisk,
                Confidence = 0.8,
                Reason = "涉及敏感路径",
                Action = SecurityAction.RequireConfirmation
            };
        }

        if (ToolClassification.SafeWriteTools.Contains(request.ToolName) || IsWriteOperation(request.OperationType)) {
            return new ClassificationResult {
                Classification = SecurityClassification.LowRisk,
                Confidence = 0.85,
                Reason = "非敏感写入操作",
                Action = SecurityAction.AutoApprove
            };
        }

        return new ClassificationResult {
            Classification = SecurityClassification.MediumRisk,
            Confidence = 0.6,
            Reason = "未知操作类型，默认中等风险",
            Action = SecurityAction.RequireConfirmation
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsReadOperation(OperationType operationType)
        => BitMask.Contains(ReadOperationMask, operationType);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsWriteOperation(OperationType operationType)
        => BitMask.Contains(WriteOperationMask, operationType);

    private static bool IsDangerousCommand(ClassificationRequest request) {
        if (!request.Parameters.TryGetValue("command", out var commandObj) || commandObj.ValueKind != JsonValueKind.String || commandObj.GetString() is not string command) {
            return false;
        }

        foreach (var regex in DangerousCommandRegexes) {
            if (regex.IsMatch(command)) {
                return true;
            }
        }

        return false;
    }

    private static bool IsSensitivePathOperation(ClassificationRequest request) {
        foreach (var kvp in request.Parameters) {
            if (kvp.Value.ValueKind == JsonValueKind.String && kvp.Value.GetString() is string strValue) {
                if (SecurityPatterns.IsSensitivePathSegment(strValue)) {
                    return true;
                }
            }
        }

        return false;
    }

}