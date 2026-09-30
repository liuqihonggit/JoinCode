namespace Mcp.Tests;

/// <summary>
/// GitHubRunStepExtractor 单元测试 — 验证步骤名提取(REST [entry.Name] 格式 + gh CLI TSV 格式)
/// </summary>
[Trait("Category", "Deterministic")]
public sealed class GitHubRunStepExtractorTests {
    [Fact]
    public void TryExtractStepName_RestFormat_ReturnsStepName() {
        var result = GitHubRunStepExtractor.TryExtractStepName("[0_Checkout.txt] building...");
        result.Should().Be("Checkout");
    }

    [Fact]
    public void TryExtractStepName_RestFormat_WithSlash_ReturnsLastSegment() {
        var result = GitHubRunStepExtractor.TryExtractStepName("[0_build/1_Test.txt] running");
        result.Should().Be("Test");
    }

    [Fact]
    public void TryExtractStepName_TsvFormat_ReturnsSecondColumn() {
        var result = GitHubRunStepExtractor.TryExtractStepName("1\tBuild\tlog line");
        result.Should().Be("Build");
    }

    [Fact]
    public void TryExtractStepName_TsvFormat_OnlyTwoColumns_ReturnsSecond() {
        var result = GitHubRunStepExtractor.TryExtractStepName("1\tCheckout");
        result.Should().Be("Checkout");
    }

    [Fact]
    public void TryExtractStepName_NoBracketNoTab_ReturnsNull() {
        var result = GitHubRunStepExtractor.TryExtractStepName("plain log line");
        result.Should().BeNull();
    }

    [Fact]
    public void TryExtractStepName_EmptyBracket_ReturnsNull() {
        // [] closeIdx=1, 不满足 closeIdx > 1,回退到 TSV 解析(无 tab)→ null
        var result = GitHubRunStepExtractor.TryExtractStepName("[] content");
        result.Should().BeNull();
    }

    [Theory]
    [InlineData("0_Checkout.txt", "Checkout")]
    [InlineData("Build.txt", "Build")]
    [InlineData("Test", "Test")]
    [InlineData("0_build/1_Test.txt", "Test")]
    [InlineData("5_Setup/2_Install/3_Run.txt", "Run")]
    public void ExtractStepNameFromEntryName_VariousFormats(string entry, string expected) {
        GitHubRunStepExtractor.ExtractStepNameFromEntryName(entry).Should().Be(expected);
    }

    [Fact]
    public void ExtractStepNameFromEntryName_NoExtension_ReturnsName() {
        GitHubRunStepExtractor.ExtractStepNameFromEntryName("0_Build").Should().Be("Build");
    }

    [Fact]
    public void ExtractStepNameFromEntryName_OnlyExtension_ReturnsEntry() {
        // ".txt" → dotIdx=0, 不满足 dotIdx > 0, 保留 ".txt"; underscoreIdx=-1
        // name.Length != 0 → 返回 ".txt"
        GitHubRunStepExtractor.ExtractStepNameFromEntryName(".txt").Should().Be(".txt");
    }

    [Fact]
    public void ExtractStepNameFromEntryName_LeadingNumberWithUnderscore_StripsPrefix() {
        GitHubRunStepExtractor.ExtractStepNameFromEntryName("3_Deploy").Should().Be("Deploy");
    }

    [Fact]
    public void ExtractStepNameFromEntryName_NoLeadingNumber_KeepsUnderscore() {
        // "Build_Test" → underscoreIdx=5, int.TryParse("Build") 失败 → 保留 "Build_Test"
        GitHubRunStepExtractor.ExtractStepNameFromEntryName("Build_Test.txt").Should().Be("Build_Test");
    }
}
