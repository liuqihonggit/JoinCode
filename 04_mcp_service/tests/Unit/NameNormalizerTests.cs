namespace Mcp.Tests;

public sealed class NameNormalizerTests
{
    [Fact]
    public void NormalizeForMcp_ReplacesInvalidChars()
    {
        NameNormalizer.NormalizeForMcp("hello world").Should().Be("hello_world");
    }

    [Fact]
    public void NormalizeForMcp_KeepsValidChars()
    {
        NameNormalizer.NormalizeForMcp("hello-world_123").Should().Be("hello-world_123");
    }

    [Fact]
    public void NormalizeForMcp_TruncatesToMaxLength()
    {
        var longName = new string('a', 100);
        var result = NameNormalizer.NormalizeForMcp(longName);
        result.Length.Should().Be(64);
    }

    [Fact]
    public void NormalizeForMcp_CustomReplacement()
    {
        NameNormalizer.NormalizeForMcp("hello world", '-').Should().Be("hello-world");
    }

    [Fact]
    public void NormalizeForMcp_CustomMaxLength()
    {
        NameNormalizer.NormalizeForMcp("abcdefgh", maxLength: 5).Should().Be("abcde");
    }

    [Fact]
    public void NormalizeForMcp_ThrowsOnNull()
    {
        Assert.Throws<ArgumentNullException>(() => NameNormalizer.NormalizeForMcp(null!));
    }

    [Fact]
    public void NormalizeForMcp_ThrowsOnEmpty()
    {
        Assert.Throws<ArgumentException>(() => NameNormalizer.NormalizeForMcp(""));
    }

    [Fact]
    public void NormalizeForMcp_ClaudeAiPrefix_DeduplicatesReplacements()
    {
        var result = NameNormalizer.NormalizeForMcp("claude.ai  server");
        result.Should().Be("claude_ai__server");
    }
}
