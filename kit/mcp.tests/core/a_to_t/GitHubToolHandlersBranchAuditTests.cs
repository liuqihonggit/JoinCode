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

        var names = CiMatrixParser.ExtractMatrixJobNames(yml);

        names.Should().Equal("InfraIO", "Downloader", "Llm");
    }

    [Fact]
    public void ExtractMatrixJobNames_EmptyYml_ReturnsEmpty() {
        var names = CiMatrixParser.ExtractMatrixJobNames("");
        names.Should().BeEmpty();
    }

    [Fact]
    public void ExtractMatrixJobNames_NoMatrix_ReturnsEmpty() {
        var yml = "jobs:\n  build:\n    runs-on: ubuntu-latest\n";
        var names = CiMatrixParser.ExtractMatrixJobNames(yml);
        names.Should().BeEmpty();
    }

    [Fact]
    public void AuditResult_AllMatch_ReportsConsistent() {
        var result = new BranchProtectionAuditResult(
            "main", ["InfraIO", "Downloader"], ["InfraIO", "Downloader"],
            ["InfraIO", "Downloader"], [], []);

        result.IsConsistent.Should().BeTrue();
        var report = result.BuildReport();
        report.Should().Contain("✅ 匹配 (2 个)");
        report.Should().Contain("无需操作");
        report.Should().NotContain("建议");
        report.Should().NotContain("警告");
    }

    [Fact]
    public void AuditResult_MissingFromProtection_ReportsSuggestion() {
        var result = new BranchProtectionAuditResult(
            "main", ["InfraIO", "Downloader", "Llm"], ["InfraIO"],
            ["InfraIO"], ["Downloader", "Llm"], []);

        var report = result.BuildReport();
        report.Should().Contain("⚠️ CI 有但保护缺 (2 个)");
        report.Should().Contain("Downloader");
        report.Should().Contain("Llm");
        report.Should().Contain("建议");
        report.Should().Contain("gh branch sync-protection");
    }

    [Fact]
    public void AuditResult_StaleInProtection_ReportsWarning() {
        var result = new BranchProtectionAuditResult(
            "main", ["InfraIO"], ["InfraIO", "OldDeleted", "StaleCheck"],
            ["InfraIO"], [], ["OldDeleted", "StaleCheck"]);

        var report = result.BuildReport();
        report.Should().Contain("❌ 保护有但 CI 无 (2 个)");
        report.Should().Contain("OldDeleted");
        report.Should().Contain("StaleCheck");
        report.Should().Contain("警告");
        report.Should().Contain("可能测试项目已删除");
    }

    [Fact]
    public void AuditResult_WithNote_IncludesNote() {
        var result = new BranchProtectionAuditResult(
            "main", ["InfraIO"], [],
            [], [], [], "分支无保护规则");

        var report = result.BuildReport();
        report.Should().Contain("分支无保护规则");
    }
}
