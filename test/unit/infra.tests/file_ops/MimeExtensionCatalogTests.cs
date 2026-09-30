namespace Infra.Tests.FileOps;

/// <summary>
/// MimeExtensionCatalog 单数据源单元测试 — 验证 MIME→扩展名映射的完整性与边界行为
/// </summary>
[Trait("Category", "Deterministic")]
public sealed class MimeExtensionCatalogTests {
    [Theory]
    [InlineData("application/pdf", "pdf")]
    [InlineData("application/json", "json")]
    [InlineData("text/csv", "csv")]
    [InlineData("text/plain", "txt")]
    [InlineData("text/html", "html")]
    [InlineData("text/markdown", "md")]
    [InlineData("application/zip", "zip")]
    [InlineData("application/gzip", "gz")]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document", "docx")]
    [InlineData("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "xlsx")]
    [InlineData("application/vnd.openxmlformats-officedocument.presentationml.presentation", "pptx")]
    [InlineData("application/msword", "doc")]
    [InlineData("application/vnd.ms-excel", "xls")]
    [InlineData("audio/mpeg", "mp3")]
    [InlineData("audio/wav", "wav")]
    [InlineData("audio/ogg", "ogg")]
    [InlineData("video/mp4", "mp4")]
    [InlineData("video/webm", "webm")]
    [InlineData("image/png", "png")]
    [InlineData("image/jpeg", "jpg")]
    [InlineData("image/gif", "gif")]
    [InlineData("image/webp", "webp")]
    [InlineData("image/svg+xml", "svg")]
    public void GetExtension_KnownMimeTypes_ReturnsCorrectExtension(string mimeType, string expected) {
        MimeExtensionCatalog.GetExtension(mimeType).Should().Be(expected);
    }

    [Fact]
    public void GetExtension_Null_ReturnsBin() {
        MimeExtensionCatalog.GetExtension(null).Should().Be("bin");
    }

    [Fact]
    public void GetExtension_EmptyString_ReturnsBin() {
        MimeExtensionCatalog.GetExtension("").Should().Be("bin");
    }

    [Fact]
    public void GetExtension_UnknownType_ReturnsBin() {
        MimeExtensionCatalog.GetExtension("application/x-unknown").Should().Be("bin");
    }

    [Fact]
    public void GetExtension_WithCharset_IgnoresParameters() {
        MimeExtensionCatalog.GetExtension("text/html; charset=utf-8").Should().Be("html");
    }

    [Fact]
    public void GetExtension_CaseInsensitive() {
        MimeExtensionCatalog.GetExtension("APPLICATION/PDF").Should().Be("pdf");
        MimeExtensionCatalog.GetExtension("Image/PNG").Should().Be("png");
        MimeExtensionCatalog.GetExtension("TEXT/HTML").Should().Be("html");
    }

    [Fact]
    public void GetExtension_WithLeadingTrailingWhitespace_Trimmed() {
        MimeExtensionCatalog.GetExtension("  application/pdf  ").Should().Be("pdf");
    }

    [Theory]
    [InlineData("application/pdf", "pdf", true)]
    [InlineData("image/png", "png", true)]
    [InlineData("application/gzip", "gz", true)]
    [InlineData("application/x-unknown", "bin", false)]
    [InlineData("", "bin", false)]
    public void TryGetExtension_HitAndMiss(string mimeType, string expectedExt, bool expectedHit) {
        var hit = MimeExtensionCatalog.TryGetExtension(mimeType, out var ext);
        hit.Should().Be(expectedHit);
        ext.Should().Be(expectedExt);
    }

    [Fact]
    public void TryGetExtension_Null_ReturnsFalseAndBin() {
        var hit = MimeExtensionCatalog.TryGetExtension(null, out var ext);
        hit.Should().BeFalse();
        ext.Should().Be("bin");
    }
}
