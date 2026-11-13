namespace JoinCode.Abstractions.Tools;

public sealed class ToolResultBuilder {
    private readonly List<ToolContent> _content = new();
    private bool _isError;
    private readonly List<EntityMetadataEntry> _entityMetadata = [];
    private ToolDiagnostic? _diagnostic;
    private ToolErrorType? _errorType;
    private ToolErrorCode? _errorCode;

    /// <summary>创建成功构建器。</summary>
    public static ToolResultBuilder Success() => new();

    /// <summary>创建错误构建器。</summary>
    public static ToolResultBuilder Error() => new() { _isError = true };

    /// <summary>
    /// 管道未产生结果的错误 — 统一3处重复的 "Pipeline did not produce a result" 消息
    /// </summary>
    public static ToolResult PipelineNoResult()
        => Error().WithText("Pipeline did not produce a result").WithErrorType(ToolErrorType.NoResult, ToolErrorCode.ExecutionFailedNoResult).Build();

    /// <summary>
    /// 安全执行工具操作 — 封装 try-catch-log-returnError 三段式，统一26+处重复模式
    /// </summary>
    public static ToolResult SafeExecute(
        Func<ToolResult> action,
        ILogger? logger,
        string operationName) {
        try {
            return action();
        } catch (Exception ex) {
            logger?.LogError(ex, "{Operation} failed", operationName);
            return Error().WithText($"{operationName}失败: {ex.Message}").WithErrorType(ToolErrorType.ExecutionFailed, ToolErrorCode.ExecutionFailedException).Build();
        }
    }

    /// <summary>
    /// 安全执行异步工具操作 — 封装 try-catch-log-returnError 三段式（异步版本）
    /// </summary>
    public static async Task<ToolResult> SafeExecuteAsync(
        Func<Task<ToolResult>> action,
        ILogger? logger,
        string operationName) {
        try {
            return await action().ConfigureAwait(false);
        } catch (Exception ex) {
            logger?.LogError(ex, "{Operation} failed", operationName);
            return Error().WithText($"{operationName}失败: {ex.Message}").WithErrorType(ToolErrorType.ExecutionFailed, ToolErrorCode.ExecutionFailedException).Build();
        }
    }

    /// <summary>添加文本内容。</summary>
    public ToolResultBuilder WithText(string text) {
        _content.Add(new ToolContent { Type = ToolContentType.Text, Text = text });
        return this;
    }

    /// <summary>添加图像内容。</summary>
    public ToolResultBuilder WithImage(string base64Data, string mediaType) {
        _content.Add(new ToolContent { Type = ToolContentType.Image, Data = base64Data, MimeType = mediaType });
        return this;
    }

    /// <summary>
    /// 添加 PDF 文档内容（base64编码，对齐 TS FileReadTool pdf 类型）
    /// </summary>
    public ToolResultBuilder WithPdf(string base64Data, long originalSize) {
        _content.Add(new ToolContent { Type = ToolContentType.Document, Data = base64Data, MimeType = "application/pdf" });
        return this;
    }

    /// <summary>
    /// 添加二进制内容引用 — 对齐 TS persistBlobToTextBlock 写盘路径
    /// </summary>
    public ToolResultBuilder WithBinary(string base64Data, string mimeType) {
        _content.Add(new ToolContent { Type = ToolContentType.Resource, Data = base64Data, MimeType = mimeType });
        return this;
    }

    /// <summary>设置错误信息。</summary>
    public ToolResultBuilder WithError(string errorMessage) {
        _isError = true;
        _content.Clear();
        _content.Add(new ToolContent { Type = ToolContentType.Text, Text = errorMessage });
        return this;
    }

    /// <summary>
    /// 设置错误类型和错误码 — 用于在 Build() 时自动生成 JSON 格式的错误文本。
    /// 如果不调用此方法，Build() 时会根据 Diagnostic.Reason 推断错误类型，或使用 ExecutionFailed 默认值。
    /// </summary>
    /// <param name="errorType">错误类型枚举</param>
    /// <param name="errorCode">精确错误码（可选）</param>
    public ToolResultBuilder WithErrorType(ToolErrorType errorType, ToolErrorCode? errorCode = null) {
        _errorType = errorType;
        _errorCode = errorCode;
        return this;
    }

    /// <summary>
    /// 附加工具执行实体元数据 — 用于回填子类 Entity 特有字段（如 ExitCode, HttpStatusCode）
    /// </summary>
    public ToolResultBuilder WithEntityMetadata(EntityMetadataEntry entry) {
        _entityMetadata.Add(entry);
        return this;
    }

    /// <summary>
    /// 批量附加工具执行实体元数据 — 供 Shell 等复用 BuildShellEntityMetadata 的调用方使用
    /// </summary>
    public ToolResultBuilder WithEntityMetadata(IEnumerable<EntityMetadataEntry> entries) {
        _entityMetadata.AddRange(entries);
        return this;
    }

    /// <summary>
    /// 附加结构化诊断信息 — 工具失败时调用，GUI 可根据 Reason/Details/Suggestions 分区域渲染。
    /// </summary>
    public ToolResultBuilder WithDiagnostic(ToolDiagnostic diagnostic) {
        _diagnostic = diagnostic;
        return this;
    }

    /// <summary>构建工具结果。当 IsError=true 时，Content[0].Text 自动转为 JSON 格式（ToolErrorJson）。</summary>
    public ToolResult Build() {
        if (!_isError) {
            return new ToolResult {
                Content = _content,
                IsError = false,
                EntityMetadata = _entityMetadata,
                Diagnostic = _diagnostic
            };
        }

        // 错误路径：将 Content[0].Text 转为 JSON 格式
        var firstText = _content.FirstOrDefault(c => c.Type == ToolContentType.Text && !string.IsNullOrEmpty(c.Text))?.Text ?? string.Empty;

        // 确定错误类型：优先使用显式设置的 _errorType，否则从 Diagnostic.Reason 推断，否则默认 ExecutionFailed
        var errorType = _errorType ?? InferErrorTypeFromDiagnostic();

        // 构建 ToolErrorJson 并序列化为 Content[0].Text
        var errorJson = ToolErrorJsonBuilder.Build(
            errorType,
            firstText,
            errorCode: _errorCode,
            details: _diagnostic?.Details,
            suggestions: _diagnostic?.Suggestions);

        var jsonText = ToolErrorJsonBuilder.Serialize(errorJson);

        // 保留原始 Content（图片等非文本内容），但将首个文本内容替换为 JSON
        var newContent = new List<ToolContent>();
        var textReplaced = false;
        foreach (var item in _content) {
            if (!textReplaced && item.Type == ToolContentType.Text) {
                newContent.Add(new ToolContent { Type = ToolContentType.Text, Text = jsonText });
                textReplaced = true;
            } else {
                newContent.Add(item);
            }
        }
        if (!textReplaced) {
            newContent = [new ToolContent { Type = ToolContentType.Text, Text = jsonText }, .. newContent];
        }

        // 确保 Diagnostic 存在（如果未显式设置，从 errorJson 创建）
        var diagnostic = _diagnostic ?? ToolDiagnostic.Create(errorType.ToValue(), firstText);

        return new ToolResult {
            Content = newContent,
            IsError = true,
            EntityMetadata = _entityMetadata,
            Diagnostic = diagnostic
        };
    }

    /// <summary>
    /// 从 Diagnostic.Reason 推断 ToolErrorType — 常见 Reason 关键词映射
    /// </summary>
    private ToolErrorType InferErrorTypeFromDiagnostic() {
        if (_diagnostic is null) return ToolErrorType.ExecutionFailed;

        var reason = _diagnostic.Reason;
        if (reason.Contains("Permission", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("权限", StringComparison.OrdinalIgnoreCase))
            return ToolErrorType.PermissionDenied;
        if (reason.Contains("NotFound", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("找不到", StringComparison.OrdinalIgnoreCase))
            return ToolErrorType.ToolNotFound;
        if (reason.Contains("Timeout", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("超时", StringComparison.OrdinalIgnoreCase))
            return ToolErrorType.Timeout;
        if (reason.Contains("Cancel", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("取消", StringComparison.OrdinalIgnoreCase))
            return ToolErrorType.Cancelled;
        if (reason.Contains("Validation", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("验证", StringComparison.OrdinalIgnoreCase))
            return ToolErrorType.SchemaValidation;
        return ToolErrorType.ExecutionFailed;
    }
}