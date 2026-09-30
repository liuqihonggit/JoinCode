namespace Infra.Tests.FileOps;

/// <summary>
/// BinaryContentTypeCatalog 单数据源单元测试 — 验证二进制 Content-Type 检测的白名单排除逻辑
/// </summary>
[Trait("Category", "Deterministic")]
public sealed class BinaryContentTypeCatalogTests {
    [Theory]
    [InlineData("application/pdf", true)]
    [InlineData("image/png", true)]
    [InlineData("image/jpeg", true)]
    [InlineData("image/gif", true)]
    [InlineData("image/webp", true)]
    [InlineData("audio/mpeg", true)]
    [InlineData("video/mp4", true)]
    [InlineData("application/zip", true)]
    [InlineData("application/octet-stream", true)]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document", true)]
    public void IsBinaryContentType_BinaryTypes_ReturnsTrue(string contentType, bool expected) {
        BinaryContentTypeCatalog.IsBinaryContentType(contentType).Should().Be(expected);
    }

    [Theory]
    [InlineData("text/html", false)]
    [InlineData("text/plain", false)]
    [InlineData("text/csv", false)]
    [InlineData("text/markdown", false)]
    [InlineData("application/json", false)]
    [InlineData("application/xml", false)]
    [InlineData("application/javascript", false)]
    [InlineData("application/x-www-form-urlencoded", false)]
    [InlineData("application/vnd.api+json", false)]
    [InlineData("application/atom+xml", false)]
    public void IsBinaryContentType_TextTypes_ReturnsFalse(string contentType, bool expected) {
        BinaryContentTypeCatalog.IsBinaryContentType(contentType).Should().Be(expected);
    }

    [Fact]
    public void IsBinaryContentType_Null_ReturnsFalse() {
        BinaryContentTypeCatalog.IsBinaryContentType(null).Should().BeFalse();
    }

    [Fact]
    public void IsBinaryContentType_EmptyString_ReturnsFalse() {
        BinaryContentTypeCatalog.IsBinaryContentType("").Should().BeFalse();
    }

    [Fact]
    public void IsBinaryContentType_WithCharset_IgnoresParameters() {
        BinaryContentTypeCatalog.IsBinaryContentType("text/html; charset=utf-8").Should().BeFalse();
        BinaryContentTypeCatalog.IsBinaryContentType("application/pdf; charset=binary").Should().BeTrue();
    }

    [Fact]
    public void IsBinaryContentType_CaseInsensitive() {
        BinaryContentTypeCatalog.IsBinaryContentType("Application/PDF").Should().BeTrue();
        BinaryContentTypeCatalog.IsBinaryContentType("TEXT/HTML").Should().BeFalse();
        BinaryContentTypeCatalog.IsBinaryContentType("Application/JSON").Should().BeFalse();
        BinaryContentTypeCatalog.IsBinaryContentType("Application/Vendor.API+JSON").Should().BeFalse();
    }

    [Fact]
    public void IsBinaryContentType_WithWhitespace_Trimmed() {
        BinaryContentTypeCatalog.IsBinaryContentType("  text/html  ").Should().BeFalse();
        BinaryContentTypeCatalog.IsBinaryContentType("  application/pdf  ").Should().BeTrue();
    }
}
