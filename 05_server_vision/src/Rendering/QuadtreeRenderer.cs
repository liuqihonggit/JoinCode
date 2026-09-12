namespace JoinCode.Vision.Rendering;

/// <summary>
/// 四叉树渲染器 — SkiaSharp 实现，画虚线网格叠加到原图
/// 渲染规格：虚线 + 线性比例宽度(线宽=baseWidth/2^depth) + 透明度(alpha→SKColor alpha)
/// </summary>
[Register(typeof(IQuadtreeRenderer), ServiceLifetime.Singleton)]
public sealed partial class QuadtreeRenderer : IQuadtreeRenderer
{
    private readonly IQuadtreeAnnotator _annotator;
    private const float BaseStrokeWidth = 2.0f;

    /// <param name="annotator">四叉树标注器（用于 ZoomAsync 重新构建子图网格）</param>
    public QuadtreeRenderer(IQuadtreeAnnotator annotator)
    {
        _annotator = annotator ?? throw new ArgumentNullException(nameof(annotator));
    }

    /// <summary>渲染虚线网格叠加到原图 — 仅渲染 alpha≠-1 的格子</summary>
    public Task<QuadtreeRenderResult> RenderAsync(string imageBase64, QuadtreeGrid grid, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imageBase64);
        ArgumentNullException.ThrowIfNull(grid);
        cancellationToken.ThrowIfCancellationRequested();

        if (!VisionBase64.TryDecode(imageBase64, out var bytes, out var decodeError))
            throw new ArgumentException($"[VIS020] {decodeError}", nameof(imageBase64));
        using var original = SKBitmap.Decode(bytes);
        if (original is null) throw new ArgumentException("[VIS020] 无法解码图片", nameof(imageBase64));

        var info = new SKImageInfo(original.Width, original.Height);
        using var surface = SKSurface.Create(info);
        using var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.DrawBitmap(original, 0, 0, SKSamplingOptions.Default);

        var strokeWidth = BaseStrokeWidth / (1 << grid.Depth);
        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = strokeWidth,
            PathEffect = SKPathEffect.CreateDash([6f, 4f], 0),
            IsAntialias = true
        };

        foreach (var cell in grid.Cells)
        {
            if (cell.Alpha <= -1) continue;
            var alphaByte = (byte)Math.Clamp(cell.Alpha * 255, 0, 255);
            paint.Color = new SKColor(255, 0, 0, alphaByte);
            canvas.DrawRect(new SKRect(cell.X, cell.Y, cell.X + cell.Width, cell.Y + cell.Height), paint);
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var resultBytes = data.ToArray();

        return Task.FromResult(new QuadtreeRenderResult(Convert.ToBase64String(resultBytes), "image/png"));
    }

    /// <summary>聚焦指定格子 — 裁剪子图 + 重新构建四叉树编码</summary>
    public async Task<QuadtreeZoomResult> ZoomAsync(
        string imageBase64,
        string cellCode,
        int imageWidth,
        int imageHeight,
        int sourceDepth,
        int targetDepth,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imageBase64);
        ArgumentException.ThrowIfNullOrWhiteSpace(cellCode);

        var (col, row) = QuadtreeEncoder.DecodeToGrid(cellCode);
        var size = 1 << sourceDepth;
        if (col < 0 || col >= size || row < 0 || row >= size)
            throw new ArgumentException($"[VIS112] 格子编码 {cellCode} 越界: 解码坐标 (col={col}, row={row}) 超出 sourceDepth={sourceDepth} 的合法范围 0..{size - 1}");
        var cellW = imageWidth / size;
        var cellH = imageHeight / size;
        var x = col * cellW;
        var y = row * cellH;

        if (!VisionBase64.TryDecode(imageBase64, out var bytes, out var decodeError))
            throw new ArgumentException($"[VIS013] {decodeError}", nameof(imageBase64));
        var subBytes = await CellCropper.CropAsync(bytes, x, y, cellW, cellH, cancellationToken).ConfigureAwait(false);
        var subBase64 = Convert.ToBase64String(subBytes);

        var grid = _annotator.BuildGrid(cellW, cellH, targetDepth);

        return new QuadtreeZoomResult(subBase64, grid, cellCode);
    }
}
