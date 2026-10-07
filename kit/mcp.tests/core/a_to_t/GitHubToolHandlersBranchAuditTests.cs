namespace Mcp.Tests;

public sealed partial class GitHubToolHandlersTests {

    [Fact]
    public void ExtractMatrixJobNames_ParsesCiYmlCorrectly() {
        var yml = """
          matrix:
            include:
              - name: InfraIO
                csproj: test/unit/infra.tests/io/Infra.IO.Tests.csproj
              - name: Downloader
                csproj: lib/downloader.tests/Downloader.Tests.csproj
              - name: Llm
                csproj: llm/llm.tests/Llm.Tests.csproj
        """;

        var names = GitHubToolHandlers.ExtractMatrixJobNames(yml);

        names.Should().Equal("InfraIO", "Downloader", "Llm");
    }

    [Fact]
    public void ExtractMatrixJobNames_EmptyYml_ReturnsEmpty() {
        var names = GitHubToolHandlers.ExtractMatrixJobNames("");
        names.Should().BeEmpty();
    }

    [Fact]
    public void ExtractMatrixJobNames_NoMatrix_ReturnsEmpty() {
        var yml = "jobs:\n  build:\n    runs-on: ubuntu-latest\n";
        var names = GitHubToolHandlers.ExtractMatrixJobNames(yml);
        names.Should().BeEmpty();
    }

    [Fact]
    public void BuildAuditSummary_AllMatch_ReportsConsistent() {
        var summary = GitHubToolHandlers.BuildAuditSummary(
            "main",
            ["InfraIO", "Downloader"],
            ["InfraIO", "Downloader"]);

        summary.Should().Contain("✅ 匹配 (2 个)");
        summary.Should().Contain("无需操作");
        summary.Should().NotContain("建议");
        summary.Should().NotContain("警告");
    }

    [Fact]
    public void BuildAuditSummary_MissingFromProtection_ReportsSuggestion() {
        var summary = GitHubToolHandlers.BuildAuditSummary(
            "main",
            ["InfraIO", "Downloader", "Llm"],
            ["InfraIO"]);

        summary.Should().Contain("⚠️ CI 有但保护缺 (2 个)");
        summary.Should().Contain("Downloader");
        summary.Should().Contain("Llm");
        summary.Should().Contain("建议");
        summary.Should().Contain("gh branch sync-protection");
    }

    [Fact]
    public void BuildAuditSummary_StaleInProtection_ReportsWarning() {
        var summary = GitHubToolHandlers.BuildAuditSummary(
            "main",
            ["InfraIO"],
            ["InfraIO", "OldDeleted", "StaleCheck"]);

        summary.Should().Contain("❌ 保护有但 CI 无 (2 个)");
        summary.Should().Contain("OldDeleted");
        summary.Should().Contain("StaleCheck");
        summary.Should().Contain("警告");
        summary.Should().Contain("可能测试项目已删除");
    }

    [Fact]
    public void BuildAuditSummary_WithNote_IncludesNote() {
        var summary = GitHubToolHandlers.BuildAuditSummary(
            "main",
            ["InfraIO"],
            [],
            "分支无保护规则");

        summary.Should().Contain("分支无保护规则");
    }
}
