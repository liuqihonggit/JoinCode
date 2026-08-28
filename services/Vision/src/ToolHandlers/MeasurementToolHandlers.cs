namespace JoinCode.Vision.ToolHandlers;

/// <summary>
/// 测量工具处理器（M4）— 长度/深度/比例测量
/// 提供 3 个 MCP 工具：measure_length/measure_depth/measure_ratio
/// 参考物不内置，LLM 联网检索（国际化考虑）；高维变换排除，标注为模型层能力
/// </summary>
[McpToolDispatch(ToolCategory.Vision)]
public class MeasurementToolHandlers
{
    private readonly ILogger<MeasurementToolHandlers>? _logger;

    /// <param name="logger">可选日志器</param>
    public MeasurementToolHandlers(ILogger<MeasurementToolHandlers>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>长度测量 — 计算两点间像素距离，LLM可联网查参考物换算真实尺寸</summary>
    [McpTool("measure_length", "计算图片中两点间的像素距离。LLM可联网检索参考物尺寸换算为真实长度。返回像素距离+角度", "vision")]
    public Task<ToolResult> MeasureLengthAsync(
        [McpToolParameter("起点X坐标（像素）", Required = true)] int x1,
        [McpToolParameter("起点Y坐标（像素）", Required = true)] int y1,
        [McpToolParameter("终点X坐标（像素）", Required = true)] int x2,
        [McpToolParameter("终点Y坐标（像素）", Required = true)] int y2,
        CancellationToken ct = default)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        var angleRadians = Math.Atan2(dy, dx);
        var angleDegrees = angleRadians * 180.0 / Math.PI;

        var sb = new StringBuilder(256);
        sb.AppendLine($"长度测量结果:");
        sb.AppendLine($"  起点: ({x1}, {y1})");
        sb.AppendLine($"  终点: ({x2}, {y2})");
        sb.AppendLine($"  像素距离: {distance:F2} px");
        sb.AppendLine($"  角度: {angleDegrees:F1}°");
        sb.AppendLine();
        sb.AppendLine("如需换算为真实尺寸，请联网检索图片中的参考物（如A4纸=297mm）计算比例。");

        return Task.FromResult(ToolResultBuilder.Success().WithText(sb.ToString()).Build());
    }

    /// <summary>颜色进深测量 — 分析区域颜色梯度估算深度，高方差=近/低方差=远</summary>
    [McpTool("measure_depth", "分析图片指定区域的颜色梯度估算深度。颜色方差大=近，小=远。返回颜色统计+深度估计", "vision")]
    public async Task<ToolResult> MeasureDepthAsync(
        [McpToolParameter("图片 base64 编码", Required = true)] string imageBase64,
        [McpToolParameter("区域左上角X（像素）", Required = true)] int x,
        [McpToolParameter("区域左上角Y（像素）", Required = true)] int y,
        [McpToolParameter("区域宽度（像素）", Required = true)] int width,
        [McpToolParameter("区域高度（像素）", Required = true)] int height,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(imageBase64))
            return ToolResultBuilder.Error().WithText("[VIS400] imageBase64 不能为空").Build();
        if (width <= 0 || height <= 0)
            return ToolResultBuilder.Error().WithText("[VIS401] 区域尺寸必须为正").Build();

        var bytes = Convert.FromBase64String(imageBase64);
        using var image = Image.Load<Rgb24>(bytes);

        if (x < 0 || y < 0 || x + width > image.Width || y + height > image.Height)
            return ToolResultBuilder.Error().WithText($"[VIS402] 区域超出图片范围 ({image.Width}x{image.Height})").Build();

        var (avgR, avgG, avgB, varR, varG, varB, gradient) = AnalyzeRegion(image, x, y, width, height);
        var totalVariance = (varR + varG + varB) / 3.0;
        var depthEstimate = totalVariance > 0 ? 100.0 / (1.0 + totalVariance / 100.0) : 100.0;

        var sb = new StringBuilder(320);
        sb.AppendLine($"颜色进深分析:");
        sb.AppendLine($"  区域: ({x},{y}) {width}x{height}");
        sb.AppendLine($"  平均颜色: R={avgR:F1} G={avgG:F1} B={avgB:F1}");
        sb.AppendLine($"  颜色方差: R={varR:F1} G={varG:F1} B={varB:F1}");
        sb.AppendLine($"  平均方差: {totalVariance:F1}");
        sb.AppendLine($"  梯度强度: {gradient:F1}");
        sb.AppendLine($"  深度估计: {depthEstimate:F1} (0=远, 100=近)");
        sb.AppendLine();
        sb.AppendLine("注: 深度估计基于颜色梯度启发式，仅供参考。精确深度需3D传感器或双目视觉。");

        return ToolResultBuilder.Success().WithText(sb.ToString()).Build();
    }

    /// <summary>长宽比测量 — 计算区域的长宽比，支持非等比测量</summary>
    [McpTool("measure_ratio", "计算区域的长宽比(width/height)。用于M4非等比测量，如屏幕比例、纸张比例等", "vision")]
    public Task<ToolResult> MeasureRatioAsync(
        [McpToolParameter("区域宽度（像素）", Required = true)] int width,
        [McpToolParameter("区域高度（像素）", Required = true)] int height,
        CancellationToken ct = default)
    {
        if (width <= 0 || height <= 0)
            return Task.FromResult(ToolResultBuilder.Error().WithText("[VIS410] 尺寸必须为正").Build());

        var ratio = (double)width / height;
        var gcd = Gcd(width, height);
        var simplifiedW = width / gcd;
        var simplifiedH = height / gcd;

        var sb = new StringBuilder(192);
        sb.AppendLine($"长宽比测量:");
        sb.AppendLine($"  尺寸: {width}x{height}");
        sb.AppendLine($"  比例: {ratio:F4}");
        sb.AppendLine($"  简化比: {simplifiedW}:{simplifiedH}");

        var commonRatio = IdentifyCommonRatio(ratio);
        if (commonRatio is not null)
            sb.AppendLine($"  匹配常见比例: {commonRatio}");

        return Task.FromResult(ToolResultBuilder.Success().WithText(sb.ToString()).Build());
    }

    /// <summary>分析区域颜色统计 — 返回平均值/方差/梯度</summary>
    private static (double AvgR, double AvgG, double AvgB, double VarR, double VarG, double VarB, double Gradient) AnalyzeRegion(
        Image<Rgb24> image, int x, int y, int width, int height)
    {
        var count = width * height;
        var sumR = 0.0; var sumG = 0.0; var sumB = 0.0;
        var sumR2 = 0.0; var sumG2 = 0.0; var sumB2 = 0.0;
        var gradientSum = 0.0;
        var gradientCount = 0;

        for (var row = y; row < y + height; row++)
        {
            for (var col = x; col < x + width; col++)
            {
                var p = image[col, row];
                sumR += p.R; sumG += p.G; sumB += p.B;
                sumR2 += (double)p.R * p.R; sumG2 += (double)p.G * p.G; sumB2 += (double)p.B * p.B;

                if (col > x)
                {
                    var left = image[col - 1, row];
                    gradientSum += Math.Abs(p.R - left.R) + Math.Abs(p.G - left.G) + Math.Abs(p.B - left.B);
                    gradientCount++;
                }
            }
        }

        var avgR = sumR / count; var avgG = sumG / count; var avgB = sumB / count;
        var varR = sumR2 / count - avgR * avgR;
        var varG = sumG2 / count - avgG * avgG;
        var varB = sumB2 / count - avgB * avgB;
        var gradient = gradientCount > 0 ? gradientSum / gradientCount : 0;

        return (avgR, avgG, avgB, varR, varG, varB, gradient);
    }

    /// <summary>最大公约数 — 用于简化比例</summary>
    private static int Gcd(int a, int b)
    {
        while (b != 0) { var t = b; b = a % b; a = t; }
        return a;
    }

    /// <summary>识别常见长宽比 — 如16:9, 4:3, 3:2, 1:1等</summary>
    private static string? IdentifyCommonRatio(double ratio)
    {
        var commonRatios = new (double Ratio, string Name)[]
        {
            (1.0, "1:1 (正方形)"),
            (1.333, "4:3 (传统屏幕)"),
            (1.5, "3:2 (摄影)"),
            (1.6, "16:10 (宽屏)"),
            (1.778, "16:9 (宽屏电视)"),
            (1.414, "√2 (A4纸)"),
            (0.5625, "9:16 (竖屏)"),
            (0.6667, "2:3 (竖版摄影)"),
            (0.75, "3:4 (竖版屏幕)"),
        };

        foreach (var (r, name) in commonRatios)
        {
            if (Math.Abs(ratio - r) < 0.02) return name;
        }
        return null;
    }
}
