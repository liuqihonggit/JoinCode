
namespace Bridge.Tests.Phase7C;

public sealed class BridgeInboundAttachmentsTests
{
    [Fact]
    public void ExtractInboundAttachments_WithAttachments_ReturnsList()
    {
        var json = """{"file_attachments":[{"file_uuid":"uuid1","file_name":"test.txt"}]}""";
        var je = JsonDocument.Parse(json).RootElement;

        var result = BridgeInboundAttachments.ExtractInboundAttachments(je);
        Assert.Single(result);
        Assert.Equal("uuid1", result[0].FileUuid);
        Assert.Equal("test.txt", result[0].FileName);
    }

    [Fact]
    public void ExtractInboundAttachments_NoAttachments_ReturnsEmptyList()
    {
        var json = """{"type":"assistant"}""";
        var je = JsonDocument.Parse(json).RootElement;

        var result = BridgeInboundAttachments.ExtractInboundAttachments(je);
        Assert.Empty(result);
    }

    [Fact]
    public void ExtractInboundAttachments_EmptyArray_ReturnsEmptyList()
    {
        var json = """{"file_attachments":[]}""";
        var je = JsonDocument.Parse(json).RootElement;

        var result = BridgeInboundAttachments.ExtractInboundAttachments(je);
        Assert.Empty(result);
    }

    [Fact]
    public void PrependPathRefs_WithPrefix_Prepends()
    {
        var result = BridgeInboundAttachments.PrependPathRefs("content", "@/path/to/file\n");
        Assert.Equal("@/path/to/file\ncontent", result);
    }

    [Fact]
    public void PrependPathRefs_EmptyPrefix_ReturnsContent()
    {
        var result = BridgeInboundAttachments.PrependPathRefs("content", "");
        Assert.Equal("content", result);
    }
}
