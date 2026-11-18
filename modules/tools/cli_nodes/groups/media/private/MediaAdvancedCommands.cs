
// media 组第二批下沉 handler — 图像/LLM/状态/voice/vcr 类。
// 全部只用 BCL + Win32 P/Invoke：HttpClient 调 LLM，BclPng 编解码，winmm 录音，文件存状态。
// jcc 主进程无需为这些工具初始化引擎或引用 SkiaSharp/LLM SDK。






namespace JoinCode.CliNode.Media;

internal static partial class MediaCommands {
    private const string DescribeSystem =
        "You are an image analysis expert. Analyze the image and return a JSON description. " +
        "Return format: {\"summary\": string, \"labels\": [{\"label\": string, \"description\": string, \"suggested_attributes\": string[]}]}. " +
        "Return only JSON, no markdown.";

    // ==================== 截图（GDI 裸像素 → BclPng） ====================

    private static async Task<int> ScreenshotAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var scope = Get(o, "scope", "screen");
        RgbaImage img;
        if (scope.Equals("window", StringComparison.OrdinalIgnoreCase)) {
            var title = Get(o, "title", "");
            var win = FindWindow(title);
            if (win is null) { Console.Out.WriteLine($"未找到标题/进程名包含 '{title}' 的窗口"); return 0; }
            img = CaptureWindow(win);
        } else if (scope.Equals("region", StringComparison.OrdinalIgnoreCase)) {
            var x = RequiredInt(o, "x", "screenshot");
            var y = RequiredInt(o, "y", "screenshot");
            var w = RequiredInt(o, "width", "screenshot");
            var h = RequiredInt(o, "height", "screenshot");
            img = CaptureRegion(x, y, w, h);
        } else {
            img = CaptureRegion(0, 0, User32.GetSystemMetrics(0), User32.GetSystemMetrics(1));
        }
        Console.Out.WriteLine(BclPng.EncodeBase64(img));
        return 0;
    }

    private static RgbaImage CaptureRegion(int x, int y, int w, int h) {
        if (w <= 0 || h <= 0) throw new ArgumentException($"截图区域尺寸必须为正: {w}x{h}");
        var hdc = User32.GetDC(IntPtr.Zero);
        var memDc = Gdi32.CreateCompatibleDC(hdc);
        var bmp = Gdi32.CreateCompatibleBitmap(hdc, w, h);
        var old = Gdi32.SelectObject(memDc, bmp);
        try {
            if (!Gdi32.BitBlt(memDc, 0, 0, w, h, hdc, x, y, 0x00CC0020)) // SRCCOPY
                throw new ArgumentException("BitBlt 截图失败");
            return DibToImage(memDc, bmp, w, h);
        } finally {
            Gdi32.SelectObject(memDc, old);
            Gdi32.DeleteObject(bmp);
            Gdi32.DeleteDC(memDc);
            User32.ReleaseDC(IntPtr.Zero, hdc);
        }
    }

    private static RgbaImage CaptureWindow(WinInfo win) {
        var w = Math.Max(1, win.Right - win.Left);
        var h = Math.Max(1, win.Bottom - win.Top);
        return CaptureRegion(win.Left, win.Top, w, h);
    }

    private static RgbaImage DibToImage(IntPtr hdc, IntPtr bmp, int w, int h) {
        // BITMAPINFOHEADER 40 字节 + 32bpp BGRA
        var bi = new byte[40];
        BinaryPrimitives.WriteInt32LittleEndian(bi.AsSpan(0, 4), 40);
        BinaryPrimitives.WriteInt32LittleEndian(bi.AsSpan(4, 4), w);
        BinaryPrimitives.WriteInt32LittleEndian(bi.AsSpan(8, 4), h);
        bi[12] = 1; bi[14] = 32; // planes=1, bitCount=32
        var pixels = new byte[w * h * 4];
        Gdi32.GetDIBits(hdc, bmp, 0, (uint)h, pixels, bi, 0);
        // BGRA → RGBA，且 GDI DIB 自下而上，翻转行序
        var rgba = new byte[w * h * 4];
        for (var y = 0; y < h; y++) {
            var src = (h - 1 - y) * w * 4;
            var dst = y * w * 4;
            for (var x = 0; x < w; x++) {
                rgba[dst + x * 4] = pixels[src + x * 4 + 2];
                rgba[dst + x * 4 + 1] = pixels[src + x * 4 + 1];
                rgba[dst + x * 4 + 2] = pixels[src + x * 4];
                rgba[dst + x * 4 + 3] = 255;
            }
        }
        return new RgbaImage { Width = w, Height = h, Pixels = rgba };
    }

    // ==================== 图像数值分析（纯 BCL 像素计算） ====================

    private static Task<int> MeasureDepthAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var img = BclPng.Decode(Required(o, "imageBase64", "measure_depth"));
        var x = RequiredInt(o, "x", "measure_depth");
        var y = RequiredInt(o, "y", "measure_depth");
        var w = RequiredInt(o, "width", "measure_depth");
        var h = RequiredInt(o, "height", "measure_depth");
        var (x0, y0, x1, y1) = (Math.Max(0, x), Math.Max(0, y), Math.Min(img.Width, x + w), Math.Min(img.Height, y + h));
        double sum = 0, sumSq = 0; var n = 0;
        for (var yy = y0; yy < y1; yy++) {
            for (var xx = x0; xx < x1; xx++) {
                var p = (yy * img.Width + xx) * 4;
                var lum = 0.299 * img.Pixels[p] + 0.587 * img.Pixels[p + 1] + 0.114 * img.Pixels[p + 2];
                sum += lum; sumSq += lum * lum; n++;
            }
        }
        if (n == 0) throw new ArgumentException("测量区域为空（坐标超出图片范围）");
        var mean = sum / n;
        var variance = Math.Max(0, sumSq / n - mean * mean);
        var stdDev = Math.Sqrt(variance);
        // 深度线索：纹理方差越大 → 越近（简化模型，与引擎原版一致的方向）
        var depthScore = 1.0 / (1.0 + stdDev / 64.0);
        Console.Out.WriteLine($"区域亮度均值={mean:F1} 标准差={stdDev:F1} 深度估计={depthScore:F3}（越小越近）");
        return Task.FromResult(0);
    }

    private static Task<int> TemporalStableContourAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var framesJson = Required(o, "framesJson", "temporal_stable_contour");
        var threshold = GetInt(o, "threshold", 30);
        List<string>? frames;
        try { frames = JsonSerializer.Deserialize(framesJson, AdvancedJsonContext.Default.ListString); }
        catch (JsonException) { throw new ArgumentException("framesJson 不是合法的字符串数组 JSON"); }
        if (frames is null || frames.Count == 0) throw new ArgumentException("framesJson 为空，至少需要 1 帧");
        var images = frames.Select(BclPng.Decode).ToList();
        var (w, h) = (images[0].Width, images[0].Height);
        var stable = new byte[w * h * 4];
        for (var i = 0; i < stable.Length; i += 4) { stable[i] = 255; stable[i + 1] = 255; stable[i + 2] = 255; stable[i + 3] = 255; }
        var stableCount = 0;
        for (var y = 0; y < h; y++) {
            for (var x = 0; x < w; x++) {
                var baseIdx = (y * w + x) * 4;
                var stablePixel = true;
                for (var f = 1; f < images.Count; f++) {
                    if (images[f].Width != w || images[f].Height != h) continue;
                    for (var c = 0; c < 3; c++) {
                        if (Math.Abs(images[0].Pixels[baseIdx + c] - images[f].Pixels[baseIdx + c]) > threshold) { stablePixel = false; break; }
                    }
                    if (!stablePixel) break;
                }
                if (stablePixel) {
                    stable[baseIdx] = 0; stable[baseIdx + 1] = 0; stable[baseIdx + 2] = 0;
                    stableCount++;
                }
            }
        }
        var result = BclPng.EncodeBase64(new RgbaImage { Width = w, Height = h, Pixels = stable });
        Console.Out.WriteLine($"稳定轮廓: {w}x{h}, 稳定像素 {stableCount}/{w * h}");
        Console.Out.WriteLine(result);
        return Task.FromResult(0);
    }

    // ==================== LLM vision 类 ====================

    private static async Task<int> ImageDescribeAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var img = Required(o, "imageBase64", "image_describe");
        var maxLabels = GetInt(o, "maxLabels", 10);
        var reply = await MediaLlm.AskVisionAsync(DescribeSystem, $"请分析这张图片，最多识别 {maxLabels} 个标签，以JSON格式返回。", img, "image/png", ct).ConfigureAwait(false);
        var result = MediaLlm.ParseLoose<ImageDescResult>(reply, AdvancedJsonContext.Default.ImageDescResult);
        if (result is null) { Console.Out.WriteLine($"LLM 响应（未解析为结构化JSON）:\n{reply}"); return 0; }
        var sb = new StringBuilder();
        sb.AppendLine($"图片摘要: {result.Summary}");
        sb.AppendLine($"标签数: {result.Labels?.Count ?? 0}");
        for (var i = 0; i < (result.Labels?.Count ?? 0); i++) {
            var label = result.Labels![i];
            sb.AppendLine($"  [{i + 1}] {label.Label} — {label.Description}");
            if (label.SuggestedAttributes?.Count > 0) sb.AppendLine($"      可下钻属性: {string.Join(", ", label.SuggestedAttributes)}");
        }
        Console.Out.Write(sb.ToString());
        return 0;
    }

    private static async Task<int> ImageDrillDownAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var img = Required(o, "imageBase64", "image_drill_down");
        var label = Required(o, "label", "image_drill_down");
        var depth = GetInt(o, "currentDepth", 0);
        var maxDepth = GetInt(o, "maxDepth", 3);
        var system = $"You analyze a specific image label in depth. Current depth {depth}/{maxDepth}. Return concise JSON with attributes.";
        var reply = await MediaLlm.AskVisionAsync(system, $"深入分析标签「{label}」的属性细节，以JSON返回。", img, "image/png", ct).ConfigureAwait(false);
        Console.Out.WriteLine(reply);
        return 0;
    }

    private static async Task<int> DetectUiElementsAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var img = Get(o, "base64Screenshot", "");
        if (img.Length == 0) img = BclPng.EncodeBase64(CaptureRegion(0, 0, User32.GetSystemMetrics(0), User32.GetSystemMetrics(1)));
        var system = "You detect UI elements in screenshots. Return JSON array of {name,type,x,y,width,height,description}.";
        var reply = await MediaLlm.AskVisionAsync(system, "列出截图中的所有 UI 元素，以 JSON 数组返回。", img, "image/png", ct).ConfigureAwait(false);
        Console.Out.WriteLine(reply);
        return 0;
    }

    private static async Task<int> FindElementAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var desc = Required(o, "description", "find_element");
        var img = Get(o, "base64Screenshot", "");
        if (img.Length == 0) img = BclPng.EncodeBase64(CaptureRegion(0, 0, User32.GetSystemMetrics(0), User32.GetSystemMetrics(1)));
        var system = "You locate a described UI element in a screenshot. Return JSON {found,x,y,width,height,confidence,description}.";
        var reply = await MediaLlm.AskVisionAsync(system, $"在截图中定位这个元素: {desc}。以 JSON 返回。", img, "image/png", ct).ConfigureAwait(false);
        Console.Out.WriteLine(reply);
        return 0;
    }

    private static async Task<int> TemporalAggregateAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var framesJson = Required(o, "framesJson", "temporal_aggregate");
        var prompt = Get(o, "analysisPrompt", "分析这些帧随时间的变化，返回 JSON 摘要。");
        var reply = await MediaLlm.AskTextAsync("You aggregate information across multiple frames. Return concise JSON.", prompt + "\n\n帧数据: " + Truncate(framesJson, 8000), ct).ConfigureAwait(false);
        Console.Out.WriteLine(reply);
        return 0;
    }

    private static async Task<int> DesktopDetectAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        Required(o, "sceneId", "desktop_detect"); // 保留参数校验，状态由场景文件承载
        var img = BclPng.EncodeBase64(CaptureRegion(0, 0, User32.GetSystemMetrics(0), User32.GetSystemMetrics(1)));
        var system = "You detect desktop UI elements for computer use. Return JSON array of {name,type,x,y,width,height}.";
        var reply = await MediaLlm.AskVisionAsync(system, "识别桌面截图中的可交互元素，以 JSON 数组返回。", img, "image/png", ct).ConfigureAwait(false);
        Console.Out.WriteLine(reply);
        return 0;
    }

    // ==================== 图像 JSON DTO ====================

    private sealed class ImageDescResult {
        public string? Summary { get; set; }
        public List<ImageLabel>? Labels { get; set; }
    }
    private sealed class ImageLabel {
        public string? Label { get; set; }
        public string? Description { get; set; }
        public List<string>? SuggestedAttributes { get; set; }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "...(truncated)";
}
