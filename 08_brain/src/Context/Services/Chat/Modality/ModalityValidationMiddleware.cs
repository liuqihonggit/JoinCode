namespace Core.Context.Modality;

/// <summary>
/// 模态验证中间件 — 检查用户消息的媒介意图是否与当前模型的模态能力匹配。
/// 不匹配时注入标准报错文本，指导 LLM 自主决策：
/// <para>1. 用 ModelSearch 工具查找支持目标功能的模型（渐进式：list_groups → map[功能Key]）</para>
/// <para>2. 通过 Agent 工具指定 model 参数创建子代理执行任务</para>
/// <para>3. 降级策略：纯文本验证 → web 工具找 OCR(≤5次) → 请求用户接管(AskUserQuestion)</para>
/// <para>管道位置：TokenBudget 之后、PreChat 之前</para>
/// </summary>
[Register(typeof(IChatMiddleware), ServiceLifetime.Singleton)]
public sealed class ModalityValidationMiddleware : IChatMiddleware
{
    private readonly IModelConfigLoader _modelConfigLoader;
    private readonly WorkflowConfig _config;
    private readonly MediaIntentDetector _detector;
    private readonly ILogger<ModalityValidationMiddleware>? _logger;

    public ModalityValidationMiddleware(
        IModelConfigLoader modelConfigLoader,
        WorkflowConfig config,
        MediaIntentDetector detector,
        ILogger<ModalityValidationMiddleware>? logger = null)
    {
        _modelConfigLoader = modelConfigLoader;
        _config = config;
        _detector = detector;
        _logger = logger;
    }

    public async IAsyncEnumerable<ChatStreamEvent> InvokeAsync(
        ChatMiddlewareContext context,
        StreamMiddlewareDelegate<ChatMiddlewareContext, ChatStreamEvent> next,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var detection = _detector.Detect(context.Message);

        if (detection.DetectedModalities == ModelModalityKind.None)
        {
            await foreach (var evt in next(context, ct).ConfigureAwait(false))
                yield return evt;
            yield break;
        }

        var vendor = _config.Provider.Vendor;
        var modelId = _config.Provider.ModelId;
        var modelModalities = _modelConfigLoader.GetModalities(vendor, modelId);

        var missing = detection.DetectedModalities & ~modelModalities;
        if (missing == ModelModalityKind.None)
        {
            await foreach (var evt in next(context, ct).ConfigureAwait(false))
                yield return evt;
            yield break;
        }

        _logger?.LogInformation(
            "模态不匹配: 模型 {ModelId} 缺失 {Missing}，检测到意图关键词: {Keywords}",
            modelId, missing, string.Join(", ", detection.MatchedKeywords));

        var missingDesc = ModalityMismatchMessageBuilder.FormatMissingModalities(missing);
        var keywordsDesc = string.Join(", ", detection.MatchedKeywords);
        var injection = ModalityMismatchMessageBuilder.Build(modelId, missing, missingDesc, keywordsDesc);
        context.ModalityMismatchInjection = injection;

        await foreach (var evt in next(context, ct).ConfigureAwait(false))
            yield return evt;
    }
}
