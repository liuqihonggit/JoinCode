namespace JoinCode.Vision.Tests;

/// <summary>
/// 图像格子裁剪器单元测试 — 裁剪尺寸/像素保留/base64
/// </summary>
public sealed class CellCropperTests
{
    [Fact]
    public async Task CropAsync_ReturnsCroppedDimensions()
    {
        var bytes = CreateTestImage(100, 100, Color.Red);

        var cropped = await CellCropper.CropAsync(bytes, 0, 0, 50, 50);

        using var img = Image.Load(cropped);
        img.Width.Should().Be(50);
        img.Height.Should().Be(50);
    }

    [Fact]
    public async Task CropAsync_PreservesPixelColor()
    {
        var bytes = CreateTestImage(100, 100, Color.Blue);

        var cropped = await CellCropper.CropAsync(bytes, 25, 25, 50, 50);

        using var img = Image.Load<Rgba32>(cropped);
        img[0, 0].B.Should().Be(255);
        img[0, 0].R.Should().Be(0);
    }

    [Fact]
    public async Task CropAsync_CropsBottomRightQuadrant()
    {
        var bytes = CreateTestImage(100, 100, Color.Green);

        var cropped = await CellCropper.CropAsync(bytes, 50, 50, 50, 50);

        using var img = Image.Load(cropped);
        img.Width.Should().Be(50);
        img.Height.Should().Be(50);
    }

    [Fact]
    public async Task CropToBase64Async_ReturnsValidBase64Png()
    {
        var bytes = CreateTestImage(100, 100, Color.Red);

        var base64 = await CellCropper.CropToBase64Async(bytes, 0, 0, 50, 50);

        base64.Should().NotBeNullOrEmpty();
        var decoded = Convert.FromBase64String(base64);
        using var img = Image.Load(decoded);
        img.Width.Should().Be(50);
    }

    [Fact]
    public async Task CropAsync_EmptyBytes_Throws()
    {
        var act = async () => await CellCropper.CropAsync([], 0, 0, 10, 10);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CropAsync_NegativeDimensions_Throws()
    {
        var bytes = CreateTestImage(100, 100, Color.Red);
        var act = async () => await CellCropper.CropAsync(bytes, 0, 0, -10, 10);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    private static byte[] CreateTestImage(int width, int height, Color color)
    {
        using var img = new Image<Rgba32>(width, height, color);
        using var ms = new MemoryStream();
        img.Save(ms, PngFormat.Instance);
        return ms.ToArray();
    }
}
