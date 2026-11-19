namespace Gh.Tests;

public sealed partial class GitHubToolHandlersTests {

    [Fact]
    public void ExtractAllCheckNames_MatrixJobWithNameField_ExpandsWithOuterPrefix() {
        var yml = """
          jobs:
            unit-tests:
              name: Unit - ${{ matrix.name }}
              strategy:
                matrix:
                  include:
                    - name: InfraIO
                      csproj: test/unit/infra.tests/io/Infra.IO.Tests.csproj
                    - name: Downloader
                      csproj: lib/downloader.tests/Downloader.Tests.csproj
                    - name: Llm
                      csproj: llm/llm.tests/Llm.Tests.csproj
        """;

        var names = CiMatrixParser.ExtractAllCheckNames(yml, "unit-tests");

        names.Should().Equal(
            "unit-tests / Unit - InfraIO",
            "unit-tests / Unit - Downloader",
            "unit-tests / Unit - Llm");
    }

    [Fact]
    public void ExtractAllCheckNames_DomainField_ExpandsCorrectly() {
        var yml = """
          jobs:
            unit-tests:
              name: Unit - ${{ matrix.domain }}
              strategy:
                matrix:
                  include:
                    - domain: generators
                      csprojs: a.csproj
                    - domain: collections
                      csprojs: b.csproj
        """;

        var checks = CiMatrixParser.ExtractAllCheckNames(yml, "unit-tests");

        checks.Should().Equal("unit-tests / Unit - generators", "unit-tests / Unit - collections");
    }

    [Fact]
    public void ExtractAllCheckNames_StandaloneJobs_KeepFullNameWithoutDashTruncation() {
        // reusable workflow 的独立 job: "Tool Nodes - declarations" 必须整体保留 —
        // 旧 ExtractMatrixNameFromCheck 会按最后一个 " - " 截成 "declarations"，误报脏检查
        var yml = """
          jobs:
            unit-tests:
              name: Unit - ${{ matrix.domain }}
              strategy:
                matrix:
                  include:
                    - domain: generators
                      csprojs: a.csproj
            module-order:
              name: Module order (CLI + GUI)
              runs-on: ubuntu-24.04
            tool-nodes-declarations:
              name: Tool Nodes - declarations
              runs-on: ubuntu-24.04
        """;

        var checks = CiMatrixParser.ExtractAllCheckNames(yml, "unit-tests");

        checks.Should().Equal(
            "unit-tests / Unit - generators",
            "unit-tests / Module order (CLI + GUI)",
            "unit-tests / Tool Nodes - declarations");
    }

    [Fact]
    public void ExtractAllCheckNames_EmptyYml_ReturnsEmpty() {
        CiMatrixParser.ExtractAllCheckNames("", "unit-tests").Should().BeEmpty();
    }

    [Fact]
    public void ExtractAllCheckNames_UnnamedLocalJob_FallsBackToJobId_AndSkipsCallerJobs() {
        // ci.yml 的 detect 无 name → check 上下文就是 job_id；build 是 uses: caller，由被调 workflow 展开，须排除
        var ciYml = """
          jobs:
            detect:
              runs-on: ubuntu-24.04
              outputs:
                non-docs: ${{ steps.detect.outputs.non-docs }}
              steps:
                - id: detect
                  run: echo ok
            build:
              needs: detect
              uses: ./.github/workflows/ci-build.yml
        """;

        var localChecks = CiMatrixParser.ExtractAllCheckNames(ciYml, null, new HashSet<string>(["build"], StringComparer.Ordinal));

        localChecks.Should().Equal("detect");
    }

    [Fact]
    public async Task AuditAsync_StandaloneJobsAndOutOfScopeChecks_NoFalseStale() {
        var fs = new InMemoryFileSystem();
        const string ymlPath = "/repo/.github/workflows/ci-unit-tests.yml";
        await fs.WriteAllText(ymlPath, """
          jobs:
            unit-tests:
              name: Unit - ${{ matrix.domain }}
              strategy:
                matrix:
                  include:
                    - domain: generators
                      csprojs: a.csproj
                    - domain: collections
                      csprojs: b.csproj
            tool-nodes-reality:
              name: Tool Nodes - reality
              runs-on: ubuntu-24.04
        """).ConfigureAwait(true);
        var api = new FakeGitHubApiClient();
        api.EnqueueResponse(new GitHubApiResponse {
            Success = true,
            StatusCode = 200,
            Body = """{"strict":true,"contexts":["build / Build","unit-tests / Unit - generators","unit-tests / Unit - collections","unit-tests / Tool Nodes - reality"]}""",
        });

        var auditor = new BranchProtectionAuditor(api, fs);
        var result = await auditor.AuditAsync("owner", "repo", "main", ymlPath).ConfigureAwait(true);

        result.IsConsistent.Should().BeTrue();
        result.Matched.Should().Equal(
            "unit-tests / Tool Nodes - reality",
            "unit-tests / Unit - collections",
            "unit-tests / Unit - generators");
        result.StaleInProtection.Should().BeEmpty();
        result.MissingFromProtection.Should().BeEmpty();
        result.Note.Should().Contain("其他 workflow 的 1 个 required check 不参与对比");
    }

    [Fact]
    public async Task AuditAsync_MissingMatrixContext_ReportsFullContextName() {
        var fs = new InMemoryFileSystem();
        const string ymlPath = "/repo/.github/workflows/ci-unit-tests.yml";
        await fs.WriteAllText(ymlPath, """
          jobs:
            unit-tests:
              name: Unit - ${{ matrix.domain }}
              strategy:
                matrix:
                  include:
                    - domain: generators
                      csprojs: a.csproj
                    - domain: tools
                      csprojs: b.csproj
        """).ConfigureAwait(true);
        var api = new FakeGitHubApiClient();
        api.EnqueueResponse(new GitHubApiResponse {
            Success = true,
            StatusCode = 200,
            Body = """{"strict":true,"contexts":["unit-tests / Unit - generators"]}""",
        });

        var auditor = new BranchProtectionAuditor(api, fs);
        var result = await auditor.AuditAsync("owner", "repo", "main", ymlPath).ConfigureAwait(true);

        result.IsConsistent.Should().BeFalse();
        result.MissingFromProtection.Should().Equal("unit-tests / Unit - tools");
        result.StaleInProtection.Should().BeEmpty();
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
        report.Should().Contain("操作引导");
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
        report.Should().Contain("操作引导");
        report.Should().Contain("自动清理");
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
