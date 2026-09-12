namespace JoinCode.Vision.ToolHandlers;

/// <summary>
/// 时序隐喻工具处理器（M3）— 多帧时序聚合 + 稳定轮廓提取
/// 提供 2 个 MCP 工具：temporal_aggregate（时序聚合）/ temporal_stable_contour（稳定轮廓）
/// 纵深防御链：L1模型原生→L2请求用户下推→L3抽帧（本工具实现L3帧分析）
/// </summary>
[McpToolDispatch(ToolCategory.Vision)]
public class TemporalMetaphorToolHandlers
{
    private readonly IQueryService _queryService;
    private readonly ILogger<TemporalMetaphorToolHandlers>? _logger;

    private static readonly ChatOptions TemporalOptions = new() { Temperature = 0.3f, MaxTokens = 4000 };
    private const int MaxFrames = 10;

    /// <param name="queryService">LLM 查询服务 — 发送多帧到多模态模型获取时序分析</param>
    /// <param name="logger">可选日志器</param>
    public TemporalMetaphorToolHandlers(IQueryService queryService, ILogger<TemporalMetaphorToolHandlers>? logger = null)
    {
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
        _logger = logger;
    }

    /// <summary>多帧时序聚合 — 将多帧图片发送给LLM分析时序演变</summary>
    [McpTool("temporal_aggregate", "分析多帧图片的时序演变。framesJson为base64数组JSON如[\"base64_1\",\"base64_2\"]。用于M3时序隐喻聚合", "vision")]
    public async Task<ToolResult> TemporalAggregateAsync(
        [McpToolParameter("帧图片base64数组的JSON，如[\"base64_1\",\"base64_2\"]", Required = true)] string framesJson,
        [McpToolParameter("分析提示词（可选），如\"描述物体的运动轨迹\"", Required = false)] string? analysisPrompt = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(framesJson))
            return ToolResultBuilder.Error().WithText("[VIS300] framesJson 不能为空").Build();

        List<string>? frames;
        try
        {
            frames = RelaxedJsonSerializer.Deserialize(framesJson, VisionJsonContext.Default.ListString);
        }
        catch (JsonException)
        {
            return ToolResultBuilder.Error().WithText("[VIS301] framesJson 解析失败或为空").Build();
        }
        if (frames is null || frames.Count == 0)
            return ToolResultBuilder.Error().WithText("[VIS301] framesJson 解析失败或为空").Build();
        if (frames.Count > MaxFrames)
            return ToolResultBuilder.Error().WithText($"[VIS302] 帧数超过上限 {MaxFrames}").Build();

        var prompt = string.IsNullOrWhiteSpace(analysisPrompt) ? "请分析这些帧的时序演变，描述变化过程。" : analysisPrompt;
        var contentBlocks = new List<ToolContent>(frames.Count);
        for (var i = 0; i < frames.Count; i++)
        {
            contentBlocks.Add(new ToolContent { Type = ToolContentType.Image, Data = frames[i], MimeType = "image/png" });
        }

        var messages = new MessageList();
        messages.AddSystemMessage(TemporalSystemPrompt);
        messages.Add(new ApiMessage(MessageRole.User, $"共 {frames.Count} 帧。{prompt}")
        {
            ContentBlocks = contentBlocks
        });

        var responseList = await _queryService.GetApiMessageContentsAsync(messages, TemporalOptions, cancellationToken: ct).ConfigureAwait(false);
        var responseText = responseList.FirstOrDefault()?.Content ?? string.Empty;

        if (string.IsNullOrWhiteSpace(responseText))
            return ToolResultBuilder.Error().WithText("[VIS303] LLM 返回空响应").Build();

        return ToolResultBuilder.Success().WithText($"时序聚合分析（{frames.Count} 帧）:\n{responseText}").Build();
    }

    /// <summary>稳定中间轮廓提取 — 帧差粗筛找到跨帧稳定区域，返回稳定区域掩码图</summary>
    [McpTool("temporal_stable_contour", "提取多帧中跨帧稳定的区域轮廓。帧差粗筛:像素差异<threshold视为稳定。返回稳定区域掩码图base64", "vision")]
    public async Task<ToolResult> TemporalStableContourAsync(
        [McpToolParameter("帧图片base64数组的JSON", Required = true)] string framesJson,
        [McpToolParameter("帧差阈值(0-255)，差异<阈值视为稳定，默认30", Required = false)] int threshold = 30,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(framesJson))
            return ToolResultBuilder.Error().WithText("[VIS310] framesJson 不能为空").Build();

        List<string>? frames;
        try
        {
            frames = RelaxedJsonSerializer.Deserialize(framesJson, VisionJsonContext.Default.ListString);
        }
        catch (JsonException)
        {
            return ToolResultBuilder.Error().WithText("[VIS311] framesJson 解析失败").Build();
        }
        if (frames is null || frames.Count < 2)
            return ToolResultBuilder.Error().WithText("[VIS311] 至少需要2帧才能计算稳定轮廓").Build();
        if (frames.Count > MaxFrames)
            return ToolResultBuilder.Error().WithText($"[VIS312] 帧数超过上限 {MaxFrames}").Build();
        if (threshold < 0 || threshold > 255)
            return ToolResultBuilder.Error().WithText("[VIS313] threshold 必须在 0-255 范围内").Build();

        try
        {
            var maskBase64 = await ComputeStableMaskAsync(frames, threshold, ct).ConfigureAwait(false);

            return ToolResultBuilder.Success()
                .WithText($"稳定轮廓提取完成: {frames.Count} 帧, 阈值={threshold}")
                .WithImage(maskBase64, "image/png")
                .Build();
        }
        catch (ArgumentException ex) when (ex.Message.StartsWith("[VIS314]", StringComparison.Ordinal) || ex.Message.StartsWith("[VIS315]", StringComparison.Ordinal))
        {
            return ToolResultBuilder.Error().WithText(ex.Message).Build();
        }
    }

    /// <summary>计算稳定区域掩码 — 帧差粗筛，稳定像素=白色，不稳定=黑色</summary>
    /// <exception cref="ArgumentException">帧尺寸不一致([VIS314])或帧base64无效([VIS315])时抛出</exception>
    private static async Task<string> ComputeStableMaskAsync(List<string> frameBase64List, int threshold, CancellationToken ct)
    {
        var frames = new List<Image<Rgb24>>(frameBase64List.Count);
        try
        {
            foreach (var base64 in frameBase64List)
            {
                if (!VisionBase64.TryDecode(base64, out var bytes, out var decodeError))
                    throw new ArgumentException($"[VIS315] 帧 base64 无效: {decodeError}");
                Image<Rgb24> frame;
                try
                {
                    frame = Image.Load<Rgb24>(bytes);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    throw new ArgumentException("[VIS315] 帧图片解码失败，请检查 base64 是否为有效图片");
                }
                frames.Add(frame);
            }

            var width = frames[0].Width;
            var height = frames[0].Height;
            for (var i = 1; i < frames.Count; i++)
            {
                if (frames[i].Width != width || frames[i].Height != height)
                    throw new ArgumentException($"[VIS314] 帧尺寸不一致: 帧0={width}x{height}, 帧{i}={frames[i].Width}x{frames[i].Height}，所有帧必须同尺寸");
            }

            using var mask = new Image<L8>(width, height, new L8(0));

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var p0 = frames[0][x, y];
                    var isStable = true;
                    for (var i = 1; i < frames.Count; i++)
                    {
                        var pi = frames[i][x, y];
                        var diff = Math.Max(Math.Max(Math.Abs(p0.R - pi.R), Math.Abs(p0.G - pi.G)), Math.Abs(p0.B - pi.B));
                        if (diff > threshold)
                        {
                            isStable = false;
                            break;
                        }
                    }
                    mask[x, y] = isStable ? new L8(255) : new L8(0);
                }
            }

            using var ms = new MemoryStream();
            await mask.SaveAsync(ms, PngFormat.Instance, ct).ConfigureAwait(false);
            return Convert.ToBase64String(ms.ToArray());
        }
        finally
        {
            foreach (var frame in frames)
                frame.Dispose();
        }
    }

    private static string TemporalSystemPrompt => $"""
        You are a temporal analysis expert. Analyze the temporal evolution across multiple frames and return a structured analysis.
        Focus on:
        1. Object motion trajectories and velocity
        2. State changes (appearance/disappearance/deformation)
        3. Stable regions and changing regions
        4. Temporal patterns (periodic/trend/sporadic)
        Return the analysis in {LocalLanguageDetector.GetNativeLanguageName(L.CurrentLanguage)}.
        """;
}
