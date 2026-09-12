namespace Mcp.Tests;

public sealed class McpBinaryHelperTests
{
    [Fact]
    public void IsBinaryContentType_Null_ReturnsFalse()
    {
        McpBinaryHelper.IsBinaryContentType(null).Should().BeFalse();
    }

    [Fact]
    public void IsBinaryContentType_Empty_ReturnsFalse()
    {
        McpBinaryHelper.IsBinaryContentType("").Should().BeFalse();
    }

    [Fact]
    public void IsBinaryContentType_Text_ReturnsFalse()
    {
        McpBinaryHelper.IsBinaryContentType("text/plain").Should().BeFalse();
    }

    [Fact]
    public void IsBinaryContentType_Json_ReturnsFalse()
    {
        McpBinaryHelper.IsBinaryContentType("application/json").Should().BeFalse();
    }

    [Fact]
    public void IsBinaryContentType_JsonWithCharset_ReturnsFalse()
    {
        McpBinaryHelper.IsBinaryContentType("application/json; charset=utf-8").Should().BeFalse();
    }

    [Fact]
    public void IsBinaryContentType_Xml_ReturnsFalse()
    {
        McpBinaryHelper.IsBinaryContentType("application/xml").Should().BeFalse();
    }

    [Fact]
    public void IsBinaryContentType_Javascript_ReturnsFalse()
    {
        McpBinaryHelper.IsBinaryContentType("application/javascript").Should().BeFalse();
    }

    [Fact]
    public void IsBinaryContentType_FormUrlEncoded_ReturnsFalse()
    {
        McpBinaryHelper.IsBinaryContentType("application/x-www-form-urlencoded").Should().BeFalse();
    }

    [Fact]
    public void IsBinaryContentType_Image_ReturnsTrue()
    {
        McpBinaryHelper.IsBinaryContentType("image/png").Should().BeTrue();
    }

    [Fact]
    public void IsBinaryContentType_OctetStream_ReturnsTrue()
    {
        McpBinaryHelper.IsBinaryContentType("application/octet-stream").Should().BeTrue();
    }

    [Fact]
    public void IsBinaryContentType_Pdf_ReturnsTrue()
    {
        McpBinaryHelper.IsBinaryContentType("application/pdf").Should().BeTrue();
    }

    [Fact]
    public void IsBinaryContentType_PlusJson_ReturnsFalse()
    {
        McpBinaryHelper.IsBinaryContentType("application/vnd.api+json").Should().BeFalse();
    }

    [Fact]
    public void IsBinaryContentType_PlusXml_ReturnsFalse()
    {
        McpBinaryHelper.IsBinaryContentType("application/vnd.api+xml").Should().BeFalse();
    }

    [Fact]
    public void IsImageMimeType_Null_ReturnsFalse()
    {
        McpBinaryHelper.IsImageMimeType(null).Should().BeFalse();
    }

    [Fact]
    public void IsImageMimeType_ImagePng_ReturnsTrue()
    {
        McpBinaryHelper.IsImageMimeType("image/png").Should().BeTrue();
    }

    [Fact]
    public void IsImageMimeType_ImageJpeg_ReturnsTrue()
    {
        McpBinaryHelper.IsImageMimeType("image/jpeg").Should().BeTrue();
    }

    [Fact]
    public void IsImageMimeType_TextPlain_ReturnsFalse()
    {
        McpBinaryHelper.IsImageMimeType("text/plain").Should().BeFalse();
    }

    [Fact]
    public void GeneratePersistId_ContainsMcpPrefix()
    {
        var id = McpBinaryHelper.GeneratePersistId("test-server");
        id.Should().StartWith("mcp-");
        id.Should().Contain("-blob-");
    }

    [Fact]
    public void GetBinaryBlobSavedMessage_ContainsInfo()
    {
        var msg = McpBinaryHelper.GetBinaryBlobSavedMessage("/tmp/file.bin", "image/png", 1024, "Downloaded ");
        msg.Should().Contain("image/png");
        msg.Should().Contain("1024 bytes");
        msg.Should().Contain("/tmp/file.bin");
    }

    [Fact]
    public void GetBinaryBlobSavedMessage_NullMimeType_ShowsUnknown()
    {
        var msg = McpBinaryHelper.GetBinaryBlobSavedMessage("/tmp/file", null, 0, "");
        msg.Should().Contain("unknown type");
    }
}
