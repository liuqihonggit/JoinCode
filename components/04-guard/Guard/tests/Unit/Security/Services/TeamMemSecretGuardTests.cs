
namespace Core.Tests.Security;

public sealed class TeamMemSecretGuardTests
{
    private const string TeamMemDir = "/home/user/.jcc/memories/team";

    [Fact]
    public void IsTeamMemPath_NullFilePath_ShouldThrowArgumentNullException()
    {
        var guard = new TeamMemSecretGuard(TeamMemDir);
        var act = () => guard.IsTeamMemPath(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("filePath");
    }

    [Fact]
    public void CheckTeamMemSecrets_NullFilePath_ShouldThrowArgumentNullException()
    {
        var guard = new TeamMemSecretGuard(TeamMemDir);
        var act = () => guard.CheckTeamMemSecrets(null!, "content");
        act.Should().Throw<ArgumentNullException>().WithParameterName("filePath");
    }

    [Fact]
    public void CheckTeamMemSecrets_NullContent_ShouldThrowArgumentNullException()
    {
        var guard = new TeamMemSecretGuard(TeamMemDir);
        var act = () => guard.CheckTeamMemSecrets("/path/file.md", null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("content");
    }

    [Fact]
    public void IsTeamMemPath_WithinTeamDir_ReturnsTrue()
    {
        var guard = new TeamMemSecretGuard(TeamMemDir);
        var result = guard.IsTeamMemPath("/home/user/.jcc/memories/team/MEMORY.md");
        result.Should().BeTrue();
    }

    [Fact]
    public void IsTeamMemPath_OutsideTeamDir_ReturnsFalse()
    {
        var guard = new TeamMemSecretGuard(TeamMemDir);
        var result = guard.IsTeamMemPath("/home/user/.jcc/memories/user/notes.md");
        result.Should().BeFalse();
    }

    [Fact]
    public void IsTeamMemPath_PrefixAttack_ReturnsFalse()
    {
        var guard = new TeamMemSecretGuard(TeamMemDir);
        // /home/user/.jcc/memories/team-evil/ 不应匹配 /home/user/.jcc/memories/team/
        var result = guard.IsTeamMemPath("/home/user/.jcc/memories/team-evil/file.md");
        result.Should().BeFalse();
    }

    [Fact]
    public void IsTeamMemPath_NullDirectory_ReturnsFalse()
    {
        var guard = new TeamMemSecretGuard(null);
        var result = guard.IsTeamMemPath("/home/user/.jcc/memories/team/MEMORY.md");
        result.Should().BeFalse();
    }

    [Fact]
    public void CheckTeamMemSecrets_SafeContent_ReturnsNull()
    {
        var guard = new TeamMemSecretGuard(TeamMemDir);
        var result = guard.CheckTeamMemSecrets("/home/user/.jcc/memories/team/MEMORY.md", "This is safe content");
        result.Should().BeNull();
    }

    [Fact]
    public void CheckTeamMemSecrets_ContainsGitHubPat_ReturnsError()
    {
        var guard = new TeamMemSecretGuard(TeamMemDir);
        var content = "My token is ghp_0123456789abcdef0123456789abcdef0123";
        var result = guard.CheckTeamMemSecrets("/home/user/.jcc/memories/team/MEMORY.md", content);
        result.Should().NotBeNull();
        result.Should().Contain("GitHub PAT");
        result.Should().Contain("team memory");
    }

    [Fact]
    public void CheckTeamMemSecrets_ContainsAWSKey_ReturnsError()
    {
        var guard = new TeamMemSecretGuard(TeamMemDir);
        var content = "AWS key: AKIAIOSFODNN7EXAMPLE";
        var result = guard.CheckTeamMemSecrets("/home/user/.jcc/memories/team/config.md", content);
        result.Should().NotBeNull();
        result.Should().Contain("AWS Access Token");
    }

    [Fact]
    public void CheckTeamMemSecrets_OutsideTeamDir_ReturnsNull()
    {
        var guard = new TeamMemSecretGuard(TeamMemDir);
        var content = "My token is ghp_0123456789abcdef0123456789abcdef0123";
        // 不在团队记忆目录中，即使包含密钥也返回 null
        var result = guard.CheckTeamMemSecrets("/home/user/project/README.md", content);
        result.Should().BeNull();
    }

    [Fact]
    public void CheckTeamMemSecrets_NullDirectory_ReturnsNull()
    {
        var guard = new TeamMemSecretGuard(null);
        var content = "My token is ghp_0123456789abcdef0123456789abcdef0123";
        var result = guard.CheckTeamMemSecrets("/home/user/.jcc/memories/team/MEMORY.md", content);
        result.Should().BeNull();
    }

    [Fact]
    public void CheckTeamMemSecrets_MultipleSecrets_ReturnsAllLabels()
    {
        var guard = new TeamMemSecretGuard(TeamMemDir);
        var content = "ghp_0123456789abcdef0123456789abcdef0123 and AKIAIOSFODNN7EXAMPLE";
        var result = guard.CheckTeamMemSecrets("/home/user/.jcc/memories/team/MEMORY.md", content);
        result.Should().NotBeNull();
        result.Should().Contain("GitHub PAT");
        result.Should().Contain("AWS Access Token");
    }

    [Fact]
    public void CheckTeamMemSecrets_PrivateKey_ReturnsError()
    {
        var guard = new TeamMemSecretGuard(TeamMemDir);
        // private-key 规则需要 64+ 字符的密钥体
        var keyBody = new string('A', 100);
        var content = $"-----BEGIN RSA PRIVATE KEY-----\n{keyBody}\n-----END RSA PRIVATE KEY-----";
        var result = guard.CheckTeamMemSecrets("/home/user/.jcc/memories/team/keys.md", content);
        result.Should().NotBeNull();
        result.Should().Contain("Private Key");
    }
}
