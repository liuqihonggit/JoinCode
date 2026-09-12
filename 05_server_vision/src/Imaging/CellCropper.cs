namespace JoinCode.Vision.Imaging;

/// <summary>
/// 图像格子裁剪器 — 用 ImageSharp 裁剪指定矩形区域，返回 PNG 字节
/// 用于 quadtree_zoom：聚焦格子 → 裁剪子图 → 重新编码
/// </summary>
public static class CellCropper
{
    /// <summary>裁剪图像指定矩形区域，返回 PNG 字节</summary>
    /// <param name="imageBytes">原图字节</param>
    /// <param name="x">裁剪区左上角 X</param>
    /// <param name="y">裁剪区左上角 Y</param>
    /// <param name="width">裁剪区宽度</param>
    /// <param name="height">裁剪区高度</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>裁剪后 PNG 字节</returns>
    public static async Task<byte[]> CropAsync(
        byte[] imageBytes,
        int x,
        int y,
        int width,
        int height,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        if (imageBytes.Length == 0) throw new ArgumentException("[VIS010] 图像字节为空", nameof(imageBytes));
        if (width <= 0 || height <= 0) throw new ArgumentException("[VIS011] 裁剪尺寸必须为正");

        using var image = Image.Load(imageBytes);
        image.Mutate(ctx => ctx.Crop(new Rectangle(x, y, width, height)));

        using var ms = new MemoryStream();
        await image.SaveAsync(ms, PngFormat.Instance, cancellationToken).ConfigureAwait(false);
        return ms.ToArray();
    }

    /// <summary>裁剪并返回 base64 PNG</summary>
    public static async Task<string> CropToBase64Async(
        byte[] imageBytes,
        int x,
        int y,
        int width,
        int height,
        CancellationToken cancellationToken = default)
    {
        var bytes = await CropAsync(imageBytes, x, y, width, height, cancellationToken).ConfigureAwait(false);
        return Convert.ToBase64String(bytes);
    }
}
