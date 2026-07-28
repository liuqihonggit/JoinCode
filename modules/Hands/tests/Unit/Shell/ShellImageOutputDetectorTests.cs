namespace Hands.Tests.Shell;

/// <summary>
/// ShellImageOutputDetector 单元测试 — 对齐 TS BashTool/utils.ts isImageOutput/parseDataUri
/// </summary>
public class ShellImageOutputDetectorTests
{
    [Fact]
    public void IsImageOutput_DataUriPng_ReturnsTrue()
    {
        Assert.True(ShellImageOutputDetector.IsImageOutput("data:image/png;base64,iVBOR"));
    }

    [Fact]
    public void IsImageOutput_DataUriJpeg_ReturnsTrue()
    {
        Assert.True(ShellImageOutputDetector.IsImageOutput("data:image/jpeg;base64,/9j/4AAQ"));
    }

    [Fact]
    public void IsImageOutput_DataUriSvg_ReturnsTrue()
    {
        Assert.True(ShellImageOutputDetector.IsImageOutput("data:image/svg+xml;base64,PHN2Zw=="));
    }

    [Fact]
    public void IsImageOutput_PlainText_ReturnsFalse()
    {
        Assert.False(ShellImageOutputDetector.IsImageOutput("Hello world"));
    }

    [Fact]
    public void IsImageOutput_EmptyString_ReturnsFalse()
    {
        Assert.False(ShellImageOutputDetector.IsImageOutput(""));
    }

    [Fact]
    public void IsImageOutput_NullString_ReturnsFalse()
    {
        Assert.False(ShellImageOutputDetector.IsImageOutput(null!));
    }

    [Fact]
    public void IsImageOutput_MissingBase64_ReturnsFalse()
    {
        Assert.False(ShellImageOutputDetector.IsImageOutput("data:image/png;hex,iVBOR"));
    }

    [Fact]
    public void IsImageOutput_MissingSemicolon_ReturnsFalse()
    {
        Assert.False(ShellImageOutputDetector.IsImageOutput("data:image/pngbase64,iVBOR"));
    }

    [Fact]
    public void IsImageOutput_WithLeadingWhitespace_ReturnsTrue()
    {
        Assert.True(ShellImageOutputDetector.IsImageOutput("  data:image/png;base64,iVBOR"));
    }

    [Fact]
    public void ParseDataUri_ValidPng_ReturnsMediaTypeAndData()
    {
        var result = ShellImageOutputDetector.ParseDataUri("data:image/png;base64,iVBORw0KGgo=");
        Assert.NotNull(result);
        Assert.Equal("image/png", result.Value.MediaType);
        Assert.Equal("iVBORw0KGgo=", result.Value.Base64Data);
    }

    [Fact]
    public void ParseDataUri_ValidJpeg_ReturnsMediaTypeAndData()
    {
        var result = ShellImageOutputDetector.ParseDataUri("data:image/jpeg;base64,/9j/4AAQ");
        Assert.NotNull(result);
        Assert.Equal("image/jpeg", result.Value.MediaType);
        Assert.Equal("/9j/4AAQ", result.Value.Base64Data);
    }

    [Fact]
    public void ParseDataUri_InvalidPrefix_ReturnsNull()
    {
        Assert.Null(ShellImageOutputDetector.ParseDataUri("not-a-data-uri"));
    }

    [Fact]
    public void ParseDataUri_MissingBase64_ReturnsNull()
    {
        Assert.Null(ShellImageOutputDetector.ParseDataUri("data:image/png;hex,iVBOR"));
    }

    [Fact]
    public void ParseDataUri_EmptyBase64Data_ReturnsNull()
    {
        Assert.Null(ShellImageOutputDetector.ParseDataUri("data:image/png;base64,"));
    }

    [Fact]
    public void ParseDataUri_NullInput_ReturnsNull()
    {
        Assert.Null(ShellImageOutputDetector.ParseDataUri(null!));
    }
}
