namespace JoinCode.Vision.ToolHandlers;

/// <summary>
/// 隐喻拓扑工具处理器（M2）— 图像细节作为超图节点，支持递归下钻
/// 提供 2 个 MCP 工具：image_describe（顶层识别）/ image_drill_down（按标签下钻）
/// 膨胀停止：token 预算(默认2000) + 层数上限(默认3) 双保险
/// </summary>
[McpToolDispatch(ToolCategory.Vision)]
public class ImageMetaphorToolHandlers
{
    private readonly IQueryService _queryService;
    private readonly ILogger<ImageMetaphorToolHandlers>? _logger;

    private static readonly ChatOptions DescribeOptions = new() { Temperature = 0.3f, MaxTokens = 4000 };

    /// <param name="queryService">LLM 查询服务 — 发送图片到多模态模型获取结构化描述</param>
    /// <param name="logger">可选日志器</param>
    public ImageMetaphorToolHandlers(IQueryService queryService, ILogger<ImageMetaphorToolHandlers>? logger = null)
    {
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
        _logger = logger;
    }

    /// <summary>顶层粗粒度识别 — 返回图片摘要 + 标签列表（含可下钻属性建议）</summary>
    [McpTool("image_describe", "分析图片返回顶层描述+标签列表。每个标签含suggested_attributes供下钻决策。用于M2隐喻拓扑的入口节点", "vision")]
    public async Task<ToolResult> ImageDescribeAsync(
        [McpToolParameter("图片 base64 编码", Required = true)] string imageBase64,
        [McpToolParameter("最大标签数，默认10", Required = false)] int maxLabels = 10,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(imageBase64))
            return ToolResultBuilder.Error().WithText("[VIS200] imageBase64 不能为空").Build();

        var messages = new MessageList();
        messages.AddSystemMessage(DescribeSystemPrompt);
        messages.Add(new ApiMessage(MessageRole.User, $"请分析这张图片，最多识别 {maxLabels} 个标签，以JSON格式返回。")
        {
            ContentBlocks = [new ToolContent { Type = ToolContentType.Image, Data = imageBase64, MimeType = "image/png" }]
        });

        var responseList = await _queryService.GetApiMessageContentsAsync(messages, DescribeOptions, cancellationToken: ct).ConfigureAwait(false);
        var responseText = responseList.FirstOrDefault()?.Content ?? string.Empty;

        if (string.IsNullOrWhiteSpace(responseText))
            return ToolResultBuilder.Error().WithText("[VIS201] LLM 返回空响应").Build();

        var result = LlmJsonHelper.Deserialize(responseText, VisionJsonContext.Default.ImageDescriptionResult, out var repairHint, _logger);

        if (result is null)
            return ToolResultBuilder.Success().WithText($"LLM 响应（未解析为结构化JSON）:\n{responseText}").Build();

        var sb = new StringBuilder(256 + result.Labels.Count * 128);
        sb.AppendLine($"图片摘要: {result.Summary}");
        sb.AppendLine($"标签数: {result.Labels.Count}");
        sb.AppendLine();
        for (var i = 0; i < result.Labels.Count; i++)
        {
            var label = result.Labels[i];
            sb.AppendLine($"  [{i + 1}] {label.Label} — {label.Description}");
            if (label.SuggestedAttributes.Count > 0)
                sb.AppendLine($"      可下钻属性: {string.Join(", ", label.SuggestedAttributes)}");
        }

        if (repairHint is not null)
            sb.AppendLine().AppendLine($"[提示] {repairHint}");

        return ToolResultBuilder.Success().WithText(sb.ToString()).Build();
    }

    /// <summary>按标签下钻 — 获取细粒度属性，受 token 预算 + 层数上限双保险约束</summary>
    [McpTool("image_drill_down", "按标签下钻获取细粒度属性。受token预算(默认2000)+层数上限(默认3)双保险约束。用于M2隐喻拓扑的递归展开", "vision")]
    public async Task<ToolResult> ImageDrillDownAsync(
        [McpToolParameter("图片 base64 编码", Required = true)] string imageBase64,
        [McpToolParameter("要下钻的标签名（如\"冰箱\"）", Required = true)] string label,
        [McpToolParameter("当前下钻深度（0=顶层，首次下钻传1）", Required = false)] int currentDepth = 1,
        [McpToolParameter("最大下钻深度，默认3", Required = false)] int maxDepth = 3,
        [McpToolParameter("token 预算上限，默认2000", Required = false)] int tokenBudget = 2000,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(imageBase64))
            return ToolResultBuilder.Error().WithText("[VIS210] imageBase64 不能为空").Build();
        if (string.IsNullOrWhiteSpace(label))
            return ToolResultBuilder.Error().WithText("[VIS211] label 不能为空").Build();

        if (currentDepth > maxDepth)
            return ToolResultBuilder.Success().WithText($"已达最大下钻深度 {maxDepth}，停止展开。标签: {label}").Build();

        var drillOptions = new ChatOptions { Temperature = 0.3f, MaxTokens = tokenBudget };
        var messages = new MessageList();
        messages.AddSystemMessage(DrillDownSystemPrompt);
        messages.Add(new ApiMessage(MessageRole.User, $"请深入分析图片中「{label}」的详细属性（当前深度 {currentDepth}/{maxDepth}），以JSON格式返回。")
        {
            ContentBlocks = [new ToolContent { Type = ToolContentType.Image, Data = imageBase64, MimeType = "image/png" }]
        });

        var responseList = await _queryService.GetApiMessageContentsAsync(messages, drillOptions, cancellationToken: ct).ConfigureAwait(false);
        var responseText = responseList.FirstOrDefault()?.Content ?? string.Empty;

        if (string.IsNullOrWhiteSpace(responseText))
            return ToolResultBuilder.Error().WithText("[VIS212] LLM 返回空响应").Build();

        var result = LlmJsonHelper.Deserialize(responseText, VisionJsonContext.Default.ImageDrillDownResult, out var repairHint, _logger);

        if (result is null)
            return ToolResultBuilder.Success().WithText($"LLM 响应（未解析为结构化JSON）:\n{responseText}").Build();

        var sb = new StringBuilder(256 + result.Attributes.Count * 96);
        sb.AppendLine($"标签: {result.Label} (深度 {currentDepth}/{maxDepth})");
        sb.AppendLine($"属性数: {result.Attributes.Count}");
        sb.AppendLine();
        foreach (var attr in result.Attributes)
        {
            sb.AppendLine($"  {attr.Name}: {attr.Value} (置信度={attr.Confidence:F2})");
        }

        if (result.SuggestedNext.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"建议下一步下钻: {string.Join(", ", result.SuggestedNext)}");
        }

        sb.AppendLine();
        sb.AppendLine(result.HasMore ? "还有更多属性可探索" : "已无更多属性");

        if (repairHint is not null)
            sb.AppendLine($"[提示] {repairHint}");

        return ToolResultBuilder.Success().WithText(sb.ToString()).Build();
    }

    private const string DescribeSystemPrompt = """
        你是一个图像分析专家。请分析图片并返回JSON格式的描述。
        返回格式：
        ```json
        {
          "summary": "图片整体描述",
          "labels": [
            {
              "label": "标签名",
              "description": "简短描述",
              "suggested_attributes": ["可下钻属性1", "可下钻属性2"]
            }
          ]
        }
        ```
        要求：
        1. summary 用一句话概括图片内容
        2. labels 包含图片中的主要对象/区域/概念
        3. suggested_attributes 列出该标签可进一步深挖的属性（如品牌、颜色、状态、数量等）
        4. 仅返回JSON，不要额外解释
        """;

    private const string DrillDownSystemPrompt = """
        你是一个图像分析专家。请深入分析图片中指定标签的详细属性。
        返回格式：
        ```json
        {
          "label": "标签名",
          "attributes": [
            {"name": "属性名", "value": "属性值", "confidence": 0.9}
          ],
          "suggested_next": ["建议下一步下钻的目标1", "目标2"],
          "has_more": true
        }
        ```
        要求：
        1. attributes 包含该标签的细粒度属性（如品牌、型号、颜色、状态等）
        2. confidence 为置信度 0..1
        3. suggested_next 列出可继续下钻的子目标
        4. has_more 表示是否还有更多属性可探索
        5. 仅返回JSON，不要额外解释
        """;
}
