namespace Core.Context.Modality;

/// <summary>
/// 模态验证中间件 — 检查用户消息的媒介意图是否与当前模型的模态能力匹配。
/// 不匹配时注入系统提示，引导 LLM 调用 AskUserQuestion 工具询问用户处理方式：
/// a) 自动委托 — 用支持该模态的模型创建子代理执行任务，结果返回当前对话
/// b) 手工指定模型 — 用户选择模型，用该模型创建子代理
/// c) 不允许（取消操作）
/// d) 用户输入内容（自由文本）
/// 管道位置：TokenBudget 之后、PreChat 之前
/// </summary>
[Register]
public sealed class ModalityValidationMiddleware : IChatMiddleware
{
    private readonly IModelConfigLoader _modelConfigLoader;
    private readonly WorkflowConfig _config;
    private readonly MediaIntentDetector _detector;
    private readonly IConfigurationService? _configService;
    private readonly ILogger<ModalityValidationMiddleware>? _logger;

    public ModalityValidationMiddleware(
        IModelConfigLoader modelConfigLoader,
        WorkflowConfig config,
        MediaIntentDetector detector,
        IConfigurationService? configService = null,
        ILogger<ModalityValidationMiddleware>? logger = null)
    {
        _modelConfigLoader = modelConfigLoader;
        _config = config;
        _detector = detector;
        _configService = configService;
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

        var candidates = BuildCandidateList(missing);
        var missingDesc = FormatMissingModalities(missing);
        var keywordsDesc = string.Join(", ", detection.MatchedKeywords);

        var injection = BuildModalityMismatchInjection(modelId, missingDesc, keywordsDesc, candidates);
        context.ModalityMismatchInjection = injection;

        await foreach (var evt in next(context, ct).ConfigureAwait(false))
            yield return evt;
    }

    private List<(string Vendor, string ModelId, string DisplayName)> BuildCandidateList(ModelModalityKind missing)
    {
        var results = new List<(string Vendor, string ModelId, string DisplayName)>();
        foreach (var provider in _modelConfigLoader.Config.Providers)
        {
            foreach (var model in provider.Value.Models)
            {
                if (model.Capabilities.Modalities.HasFlag(missing))
                {
                    results.Add((provider.Key, model.Id, model.DisplayName));
                }
            }
        }

        var history = LoadModelHistory();
        if (history.Count > 0)
        {
            results.Sort((a, b) =>
            {
                var idxA = history.IndexOf(a.ModelId);
                var idxB = history.IndexOf(b.ModelId);
                var rankA = idxA >= 0 ? idxA : int.MaxValue;
                var rankB = idxB >= 0 ? idxB : int.MaxValue;
                return rankA.CompareTo(rankB);
            });
        }

        return results;
    }

    private List<string> LoadModelHistory()
    {
        if (_configService is null) return [];
        try
        {
            var raw = _configService.GetAsync("modelHistory", CancellationToken.None).GetAwaiter().GetResult();
            if (string.IsNullOrEmpty(raw)) return [];
            return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }
        catch
        {
            return [];
        }
    }

    private static string FormatMissingModalities(ModelModalityKind missing)
    {
        var parts = new List<string>();
        if (missing.HasFlag(ModelModalityKind.ReadImage)) parts.Add("图片识别");
        if (missing.HasFlag(ModelModalityKind.ReadGif)) parts.Add("动图识别");
        if (missing.HasFlag(ModelModalityKind.ReadVideo)) parts.Add("视频识别");
        if (missing.HasFlag(ModelModalityKind.ReadAudio)) parts.Add("音频识别");
        if (missing.HasFlag(ModelModalityKind.ReadPdf)) parts.Add("PDF识别");
        if (missing.HasFlag(ModelModalityKind.GenerateImage)) parts.Add("图片生成");
        if (missing.HasFlag(ModelModalityKind.GenerateVideo)) parts.Add("视频生成");
        if (missing.HasFlag(ModelModalityKind.GenerateAudio)) parts.Add("音频生成");
        return string.Join("、", parts);
    }

    private static string BuildModalityMismatchInjection(
        string currentModelId,
        string missingDesc,
        string keywordsDesc,
        List<(string Vendor, string ModelId, string DisplayName)> candidates)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[模态不匹配提示] 当前模型 {currentModelId} 不支持 {missingDesc}（检测到用户意图: {keywordsDesc}）。");
        sb.AppendLine("请立即使用 AskUserQuestion 工具询问用户如何处理，提供以下4个选项：");

        sb.AppendLine("  选项1: 自动委托 — 用支持该模态的模型创建子代理执行任务，结果返回当前对话");
        if (candidates.Count > 0)
        {
            var best = candidates[0];
            sb.AppendLine($"    （推荐模型: {best.DisplayName} ({best.Vendor}), ID: {best.ModelId}）");
        }

        sb.AppendLine("  选项2: 手工指定模型 — 用户从支持该模态的模型列表中选择，用该模型创建子代理");
        sb.Append("    可选模型: ");
        sb.AppendLine(string.Join(", ", candidates.Take(5).Select(c => $"{c.DisplayName}({c.Vendor}) ID:{c.ModelId}")));

        sb.AppendLine("  选项3: 不允许 — 取消操作，不处理此媒介");
        sb.AppendLine("  选项4: 用户输入内容 — 用户自由输入文本说明");
        sb.AppendLine();

        sb.AppendLine("重要：不要使用 /model 切换模型（会丢失当前对话上下文）。正确做法是使用 Agent 工具创建子代理：");
        sb.AppendLine("  - 调用 Agent 工具，设置 model 参数为目标模型ID，prompt 参数为用户原始请求");
        sb.AppendLine("  - 子代理会在目标模型上执行任务，完成后将结果返回当前对话");
        sb.AppendLine("  - 当前对话上下文完整保留，无需切回");
        sb.AppendLine();
        sb.AppendLine("示例：用户选择自动委托时，调用 Agent 工具：");
        if (candidates.Count > 0)
        {
            sb.AppendLine($"  {{\"description\": \"处理{missingDesc}\", \"prompt\": \"<用户原始请求>\", \"model\": \"{candidates[0].ModelId}\"}}");
        }

        return sb.ToString();
    }
}
