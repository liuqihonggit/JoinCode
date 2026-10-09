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

    [Fact]
    public void ComputeSyncDiff_AddsMissingCiChecks_RemovesStaleChecks() {
        var existingContexts = new List<string> { "build / Build", "unit-tests / Unit - Abs", "unit-tests / Unit - Old" };
        var ciCheckNames = new List<string> { "build / Build", "unit-tests / Unit - Abs", "unit-tests / Unit - Downloader" };

        var (added, removed, newContexts) = GitHubToolHandlers.ComputeSyncDiff(existingContexts, ciCheckNames);

        added.Should().Contain("unit-tests / Unit - Downloader");
        removed.Should().Contain("unit-tests / Unit - Old");
        newContexts.Should().Contain("build / Build");
        newContexts.Should().Contain("unit-tests / Unit - Abs");
        newContexts.Should().Contain("unit-tests / Unit - Downloader");
        newContexts.Should().NotContain("unit-tests / Unit - Old");
    }

    [Fact]
    public void ComputeSyncDiff_NoStaleChecks_ReturnsEmptyRemoved() {
        var existingContexts = new List<string> { "build / Build", "unit-tests / Unit - Abs" };
        var ciCheckNames = new List<string> { "build / Build", "unit-tests / Unit - Abs", "unit-tests / Unit - Downloader" };

        var (added, removed, newContexts) = GitHubToolHandlers.ComputeSyncDiff(existingContexts, ciCheckNames);

        added.Should().Contain("unit-tests / Unit - Downloader");
        removed.Should().BeEmpty();
        newContexts.Should().HaveCount(3);
    }

    [Fact]
    public void BuildFullProtectionPutBody_PreservesEnforceAdmins() {
        var protectionJson = """
            {"required_status_checks":{"strict":false,"contexts":["build / Build","unit-tests / Unit - Abs"]},"enforce_admins":{"enabled":true},"required_linear_history":{"enabled":false},"allow_force_pushes":{"enabled":false},"allow_deletions":{"enabled":false},"block_creations":{"enabled":false},"required_conversation_resolution":{"enabled":false},"lock_branch":{"enabled":false},"allow_fork_syncing":{"enabled":false}}
            """;
        var newContexts = new List<string> { "build / Build", "unit-tests / Unit - Abs", "unit-tests / Unit - Downloader" };

        var body = GitHubToolHandlers.BuildFullProtectionPutBody(protectionJson, newContexts);

        body.Should().Contain("\"enforce_admins\":true");
        body.Should().Contain("\"strict\":false");
        body.Should().Contain("unit-tests / Unit - Downloader");
        body.Should().Contain("\"required_pull_request_reviews\":null");
        body.Should().Contain("\"restrictions\":null");
    }
}
