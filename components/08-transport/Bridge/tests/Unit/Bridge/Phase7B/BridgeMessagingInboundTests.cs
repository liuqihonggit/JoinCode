
namespace Bridge.Tests.Phase7B;

public sealed class BridgeMessagingInboundTests
{
    [Fact]
    public void ExtractInboundMessageFields_ValidMessage_ExtractsFields()
    {
        var json = """{"type":"user","content":"hello","uuid":"msg-123"}""";
        var je = JsonDocument.Parse(json).RootElement;

        var fields = BridgeMessaging.ExtractInboundMessageFields(je);
        Assert.NotNull(fields);
        Assert.Equal("hello", fields.Content);
        Assert.Equal("msg-123", fields.Uuid);
    }

    [Fact]
    public void ExtractInboundMessageFields_EmptyObject_ReturnsNull()
    {
        // 缺少 type 字段时返回 null
        var json = "{}";
        var je = JsonDocument.Parse(json).RootElement;

        var fields = BridgeMessaging.ExtractInboundMessageFields(je);
        Assert.Null(fields);
    }

    [Fact]
    public void NormalizeImageBlocks_WithBase64Image_DetectsFormat()
    {
        // PNG magic bytes base64
        var pngBase64 = "iVBORw0KGgo=".AsSpan(); // PNG header start
        var format = BridgeMessaging.DetectImageFormatFromBase64(pngBase64);
        Assert.Equal("image/png", format);
    }

    [Fact]
    public void NormalizeImageBlocks_WithJpegImage_DetectsFormat()
    {
        // JPEG magic bytes base64 — 需要足够长度以通过 padding 检查
        var jpegBase64 = "/9j/4AAQSkY=".AsSpan(); // JPEG header start + padding
        var format = BridgeMessaging.DetectImageFormatFromBase64(jpegBase64);
        Assert.Equal("image/jpeg", format);
    }

    [Fact]
    public void NormalizeImageBlocks_UnknownFormat_ReturnsNull()
    {
        var unknownBase64 = "dGVzdA==".AsSpan(); // "test" in base64
        var format = BridgeMessaging.DetectImageFormatFromBase64(unknownBase64);
        Assert.Null(format);
    }
}
