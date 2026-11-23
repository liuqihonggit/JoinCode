namespace Guard.Security.Tests;

/// <summary>
/// 团队记忆写入前密钥门禁 — 路径判定必须真的生效。
/// 历史缺陷：守卫构造时团队目录恒为 null，<c>IsTeamMemPath</c> 永远 false，
/// 于是「已接线」的写入前检查从未拦下任何内容（TASK119/TASK124）。
/// </summary>
public sealed class TeamMemSecretGuardTests {
    private static readonly string MemdirRoot = Path.Combine(Path.GetTempPath(), "jcc-memdir-test");

    private static TeamMemSecretGuard CreateSut() =>
        new(new WorkflowConfig { MemdirPath = MemdirRoot });

    private static string TeamFile(params string[] segments) =>
        Path.Combine([TeamMemoryLayout.ResolveRoot(MemdirRoot), .. segments]);

    [Fact]
    public async Task IsTeamMemPath_FileUnderTeams_ReturnsTrue() {
        await using var sut = CreateSut();

        sut.IsTeamMemPath(TeamFile("teams", "team-a", "shared", "notes.md")).Should().BeTrue();
        sut.IsTeamMemPath(TeamFile("teams", "team-a", "members", "u1", "notes.md")).Should().BeTrue();
    }

    [Fact]
    public async Task IsTeamMemPath_RootItself_ReturnsTrue() {
        await using var sut = CreateSut();

        sut.IsTeamMemPath(TeamMemoryLayout.ResolveRoot(MemdirRoot)).Should().BeTrue();
    }

    [Fact]
    public async Task IsTeamMemPath_SiblingPrefix_ReturnsFalse() {
        await using var sut = CreateSut();
        var sibling = TeamMemoryLayout.ResolveRoot(MemdirRoot) + "-evil";

        sut.IsTeamMemPath(Path.Combine(sibling, "notes.md")).Should().BeFalse(
            "前缀攻击：team-memories-evil 不属于团队记忆目录");
    }

    [Fact]
    public async Task IsTeamMemPath_OutsideTeamRoot_ReturnsFalse() {
        await using var sut = CreateSut();

        sut.IsTeamMemPath(Path.Combine(MemdirRoot, "notes.md")).Should().BeFalse();
        sut.IsTeamMemPath(Path.Combine(Path.GetTempPath(), "other", "notes.md")).Should().BeFalse();
    }

    [Fact]
    public async Task TryGetTeamMemSecretViolation_TeamFileWithSecret_RejectsWithoutEchoingSecret() {
        await using var sut = CreateSut();
        const string secret = "AKIAABCDEFGHIJKLMNOP";

        var hit = sut.TryGetTeamMemSecretViolation(
            TeamFile("teams", "team-a", "shared", "notes.md"), $"aws_key={secret}", out var violation);

        hit.Should().BeTrue();
        var violationText = violation ?? string.Empty;
        violationText.Should()
            .NotBeEmpty()
            .And.Contain("team memory")
            .And.NotContain(secret, "错误信息只回规则名，不得回显密钥原文");
    }

    [Fact]
    public async Task TryGetTeamMemSecretViolation_TeamFileClean_Allows() {
        await using var sut = CreateSut();

        var hit = sut.TryGetTeamMemSecretViolation(
            TeamFile("teams", "team-a", "shared", "notes.md"), "普通团队约定：提交前跑测试。", out var violation);

        hit.Should().BeFalse();
        violation.Should().BeNull();
    }

    [Fact]
    public async Task TryGetTeamMemSecretViolation_NonTeamFileWithSecret_Allows() {
        await using var sut = CreateSut();

        var hit = sut.TryGetTeamMemSecretViolation(
            Path.Combine(MemdirRoot, "personal", "notes.md"), "aws_key=AKIAABCDEFGHIJKLMNOP", out var violation);

        hit.Should().BeFalse("门禁只作用于团队共享区，个人记忆写入不受影响");
        violation.Should().BeNull();
    }

    [Fact]
    public async Task DefaultConfig_UsesMemdirDefaultRoot() {
        await using var sut = new TeamMemSecretGuard();

        sut.IsTeamMemPath(Path.Combine(
            AppDataConstants.Paths.MemdirDirectory,
            TeamMemoryLayout.RootDirectoryName,
            "teams", "team-a", "shared", "notes.md")).Should().BeTrue();
    }
}
