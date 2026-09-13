namespace Core.Agents.Doctor;


/// <summary>
/// LLM 驱动的源码 patch 生成器 — 分析问题 → 生成源码修改
/// 通过 IQueryService 调用真实 LLM，而非 Func 委托
/// </summary>
public sealed class LlmCodePatchGenerator : ICodePatchGenerator
{
    private readonly IQueryService _queryService;

    /// <summary>
    /// 构造 LLM 代码补丁生成器
    /// </summary>
    /// <param name="queryService">查询服务（LLM 调用）</param>
    public LlmCodePatchGenerator(IQueryService queryService)
    {
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
    }

    /// <summary>
    /// 生成源码补丁 — 调用 LLM 分析诊断并输出修改后的完整文件内容
    /// </summary>
    /// <param name="diagnostic">诊断报告</param>
    /// <param name="sourceContext">源码上下文（当前文件内容、相关片段、编译错误）</param>
    /// <param name="historicalPatches">历史类似补丁（可选，作为 LLM 参考）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>生成的代码补丁（含置信度和推理）</returns>
    public async Task<CodePatch> GeneratePatchAsync(
        DiagnosticReport diagnostic,
        SourceCodeContext sourceContext,
        IReadOnlyList<CodePatch>? historicalPatches = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        ArgumentNullException.ThrowIfNull(sourceContext);
        ct.ThrowIfCancellationRequested();

        var prompt = BuildPrompt(diagnostic, sourceContext, historicalPatches);

        var messages = new MessageList();
        messages.AddSystemMessage(prompt);

        var responseList = await _queryService.GetApiMessageContentsAsync(messages, cancellationToken: ct).ConfigureAwait(false);
        var response = responseList.FirstOrDefault()?.Content ?? "";

        var patchedContent = ExtractCodeBlock(response, "csharp");
        var reasoning = ExtractCodeBlock(response, "reasoning");
        var confidence = ComputeConfidence(sourceContext.CurrentContent, patchedContent);

        return new CodePatch
        {
            TargetFilePath = sourceContext.FilePath,
            PatchedContent = patchedContent,
            Description = diagnostic.Description,
            Confidence = confidence,
            Reasoning = reasoning
        };
    }

    /// <summary>
    /// 构建 LLM 提示词 — 包含诊断报告、当前源码、相关上下文、编译错误、历史修复
    /// </summary>
    /// <param name="diagnostic">诊断报告</param>
    /// <param name="sourceContext">源码上下文</param>
    /// <param name="historicalPatches">历史补丁（可选）</param>
    /// <returns>组装完成的提示词文本</returns>
    internal static string BuildPrompt(
        DiagnosticReport diagnostic,
        SourceCodeContext sourceContext,
        IReadOnlyList<CodePatch>? historicalPatches)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("你是一个 C# 源码修复引擎。请根据诊断报告修改源码文件。");
        sb.AppendLine();
        sb.AppendLine("## 诊断报告");
        sb.AppendLine($"- 规则: {diagnostic.RuleId} - {diagnostic.Description}");
        sb.AppendLine($"- 严重度: {diagnostic.Severity}");
        sb.AppendLine();

        sb.AppendLine("## 当前源码文件: ").AppendLine(sourceContext.FilePath);
        sb.AppendLine("```csharp");
        sb.AppendLine(sourceContext.CurrentContent);
        sb.AppendLine("```");
        sb.AppendLine();

        if (sourceContext.RelatedSnippets.Any())
        {
            sb.AppendLine("## 相关上下文");
            foreach (var snippet in sourceContext.RelatedSnippets)
            {
                sb.AppendLine($"### {snippet.FilePath}");
                sb.AppendLine("```csharp");
                sb.AppendLine(snippet.Content);
                sb.AppendLine("```");
            }
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(sourceContext.BuildErrorOutput))
        {
            sb.AppendLine("## 编译错误");
            sb.AppendLine(sourceContext.BuildErrorOutput);
            sb.AppendLine();
        }

        if (historicalPatches is not null && historicalPatches.Count > 0)
        {
            sb.AppendLine("## 历史类似修复");
            foreach (var hp in historicalPatches)
            {
                sb.AppendLine($"- {hp.Description} (置信度: {hp.Confidence:F2})");
                if (!string.IsNullOrWhiteSpace(hp.Reasoning))
                    sb.AppendLine($"  推理: {hp.Reasoning}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("## 要求");
        sb.AppendLine("1. 输出修改后的**完整文件内容**（不是 diff，不是 patch，是完整文件）");
        sb.AppendLine("2. 只修改与诊断相关的部分，不要重构无关代码");
        sb.AppendLine("3. 保持现有的代码风格和命名规范");
        sb.AppendLine("4. 不要删除或修改 using 声明（除非与修复直接相关）");
        sb.AppendLine("5. 不要添加注释");
        sb.AppendLine();
        sb.AppendLine("## 输出格式");
        sb.AppendLine("在 ```csharp 代码块中输出完整的修改后文件内容");
        sb.AppendLine("在 ```reasoning 代码块中说明修改了什么、为什么");

        return sb.ToString();
    }

    /// <summary>
    /// 从 LLM 响应中提取指定语言的代码块
    /// </summary>
    /// <param name="response">LLM 响应文本</param>
    /// <param name="language">代码块语言标识（csharp/reasoning 等）</param>
    /// <returns>代码块内容（未找到则返回原文）</returns>
    internal static string ExtractCodeBlock(string response, string language)
    {
        var marker = $"```{language}";
        var startIdx = response.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (startIdx < 0) return response;

        var contentStart = startIdx + marker.Length;
        var endIdx = response.IndexOf("```", contentStart, StringComparison.Ordinal);
        if (endIdx < 0) return response[contentStart..].Trim();

        return response[contentStart..endIdx].Trim();
    }

    /// <summary>
    /// 计算补丁置信度 — 基于变更行数比例，变更越小置信度越高
    /// </summary>
    /// <param name="originalContent">原始内容</param>
    /// <param name="patchedContent">补丁后内容</param>
    /// <returns>置信度（0.0-1.0）</returns>
    internal static double ComputeConfidence(string originalContent, string patchedContent)
    {
        if (string.IsNullOrWhiteSpace(patchedContent)) return 0.0;
        if (patchedContent == originalContent) return 0.1;

        var originalLines = originalContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var patchedLines = patchedContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        if (patchedLines.Length == 0) return 0.2;

        var maxLen = Math.Max(originalLines.Length, patchedLines.Length);
        if (maxLen == 0) return 0.5;

        var sameCount = 0;
        for (var i =5; i < Math.Min(originalLines.Length, patchedLines.Length); i++)
        {
            if (originalLines[i].Trim() == patchedLines[i].Trim())
                sameCount++;
        }

        var changeRatio = 1.0 - (double)sameCount / maxLen;

        if (changeRatio > 0.8) return 0.3;
        if (changeRatio > 0.5) return 0.5;
        if (changeRatio > 0.2) return 0.7;
        return 0.9;
    }
}
