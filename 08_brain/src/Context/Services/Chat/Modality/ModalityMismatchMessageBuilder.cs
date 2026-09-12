namespace Core.Context.Modality;

/// <summary>
/// 模态不匹配标准报错文本构建器 — 统一 ModalityValidationMiddleware 和 SessionController 的报错格式。
/// <para>标准报错：当前模型不支持 XX → ModelSearch 查模型 → Agent 子代理执行 → 降级策略</para>
/// </summary>
public static class ModalityMismatchMessageBuilder
{
    /// <summary>
    /// 格式化缺失功能的中文描述
    /// </summary>
    public static string FormatMissingModalities(ModelModalityKind missing)
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

    /// <summary>
    /// 获取 missing 中每个功能位对应的 ModelSearch 功能Key（[EnumValue] 字符串，如 generateImage/readImage）
    /// </summary>
    public static List<string> GetMissingModalityKeys(ModelModalityKind missing)
    {
        var keys = new List<string>();
        if (missing.HasFlag(ModelModalityKind.ReadImage)) keys.Add(ModelModalityKind.ReadImage.ToValue());
        if (missing.HasFlag(ModelModalityKind.ReadGif)) keys.Add(ModelModalityKind.ReadGif.ToValue());
        if (missing.HasFlag(ModelModalityKind.ReadVideo)) keys.Add(ModelModalityKind.ReadVideo.ToValue());
        if (missing.HasFlag(ModelModalityKind.ReadAudio)) keys.Add(ModelModalityKind.ReadAudio.ToValue());
        if (missing.HasFlag(ModelModalityKind.ReadPdf)) keys.Add(ModelModalityKind.ReadPdf.ToValue());
        if (missing.HasFlag(ModelModalityKind.GenerateImage)) keys.Add(ModelModalityKind.GenerateImage.ToValue());
        if (missing.HasFlag(ModelModalityKind.GenerateVideo)) keys.Add(ModelModalityKind.GenerateVideo.ToValue());
        if (missing.HasFlag(ModelModalityKind.GenerateAudio)) keys.Add(ModelModalityKind.GenerateAudio.ToValue());
        return keys;
    }

    /// <summary>
    /// 构建标准报错注入文本 — 指导 LLM 用 ModelSearch 查模型 → Agent 子代理执行 → 降级策略
    /// </summary>
    public static string Build(
        string currentModelId,
        ModelModalityKind missing,
        string missingDesc,
        string keywordsDesc)
    {
        var keys = GetMissingModalityKeys(missing);
        var sb = new StringBuilder();
        sb.AppendLine($"[模态不匹配] 当前模型 {currentModelId} 不支持 {missingDesc}（检测到用户意图: {keywordsDesc}）。");
        sb.AppendLine();
        sb.AppendLine("请按以下步骤处理：");
        sb.AppendLine();
        sb.AppendLine("步骤1 — 查找支持该功能的模型：");
        sb.AppendLine("  调用 ModelSearch 工具，先用 query=\"list_groups\" 查看所有功能分组；");
        foreach (var key in keys)
        {
            sb.AppendLine($"  再用 query=\"map[{key}]\" 下钻查看支持该功能的模型列表（格式: vendor/modelId (DisplayName)）。");
        }
        sb.AppendLine();
        sb.AppendLine("步骤2 — 创建子代理执行任务：");
        sb.AppendLine("  从 ModelSearch 结果中选择合适的模型，记录其 modelId。");
        sb.AppendLine("  调用 Agent 工具创建子代理：");
        sb.AppendLine($"    {{\"description\": \"处理{missingDesc}\", \"prompt\": \"<用户原始请求>\", \"model\": \"<查到的modelId>\"}}");
        sb.AppendLine("  子代理会在目标模型上执行任务，结果返回当前对话，当前上下文完整保留。");
        sb.AppendLine();
        sb.AppendLine("重要：不要使用 /model 切换模型（会丢失当前对话上下文）。正确做法是使用 Agent 工具创建子代理。");
        sb.AppendLine();
        sb.AppendLine("如果支持列表缺失，或模型不可用，渐进式进行以下降级：");
        sb.AppendLine("  1. 以纯文本方式对任务进行验证（描述图片内容/用文字完成生图需求的替代方案）");
        sb.AppendLine("  2. 通过 WebSearch/WebFetch 工具查找免费 OCR/图片识别网站（如 OCR.space、百度OCR 等），最多尝试 5 次");
        sb.AppendLine("  3. 请求用户接管：调用 AskUserQuestion 询问用户如何处理");
        sb.AppendLine();

        return sb.ToString();
    }
}
