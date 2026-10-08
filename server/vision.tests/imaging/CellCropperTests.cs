namespace JoinCode.Vision.Tests;

/// <summary>
/// 图像格子裁剪器单元测试 — 裁剪尺寸/像素保留/base64
/// </summary>
public sealed class CellCropperTests {
    [Fact]
    public async Task CropAsync_ReturnsCroppedDimensions() {
        var bytes = await CreateTestImage(100, 100, SKColors.Red);

        var cropped = await CellCropper.CropAsync(bytes, 0, 0, 50, 50);

        using var img = SKBitmap.Decode(cropped);
        img!.Width.Should().Be(50);
        img.Height.Should().Be(50);
    }

    [Fact]
    public async Task CropAsync_PreservesPixelColor() {
        var bytes = await CreateTestImage(100, 100, SKColors.Blue);

        var cropped = await CellCropper.CropAsync(bytes, 25, 25, 50, 50);

        using var img = SKBitmap.Decode(cropped);
        var pixel = img!.GetPixel(0, 0);
        pixel.Blue.Should().Be(255);
        pixel.Red.Should().Be(0);
    }

    [Fact]
    public async Task CropAsync_CropsBottomRightQuadrant() {
        var bytes = await CreateTestImage(100, 100, SKColors.Green);

        var cropped = await CellCropper.CropAsync(bytes, 50, 50, 50, 50);

        using var img = SKBitmap.Decode(cropped);
        img!.Width.Should().Be(50);
        img.Height.Should().Be(50);
    }

    [Fact]
    public async Task CropToBase64Async_ReturnsValidBase64Png() {
        var bytes = await CreateTestImage(100, 100, SKColors.Red);

        var base64 = await CellCropper.CropToBase64Async(bytes, 0, 0, 50, 50);

        base64.Should().NotBeNullOrEmpty();
        var decoded = Convert.FromBase64String(base64);
        using var img = SKBitmap.Decode(decoded);
        img!.Width.Should().Be(50);
    }

    [Fact]
    public async Task CropAsync_EmptyBytes_Throws() {
        var act = async () => await CellCropper.CropAsync([], 0, 0, 10, 10);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CropAsync_NegativeDimensions_Throws() {
        var bytes = await CreateTestImage(100, 100, SKColors.Red);
        var act = async () => await CellCropper.CropAsync(bytes, 0, 0, -10, 10);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    private static Task<byte[]> CreateTestImage(int width, int height, SKColor color) {
        using var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(color);
        canvas.Flush();
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return Task.FromResult(data.ToArray());
    }
}