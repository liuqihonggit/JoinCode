namespace Composition.Tests.Commands;

/// <summary>
/// InstallGitHubAppCommand B3 手动 exe 冒烟验收 — 真实 git 进程 + 临时仓库,非 mock
/// <para>ADR 0080: 真实运行暴露,验证恶意 git remote URL 经 GetCurrentRepoAsync 解析+白名单校验后被拒</para>
/// <para>标记 Category=Integration,CI unit-tests matrix 排除(filter Category!=Integration),手动或 integration CI 触发</para>
/// </summary>
[Trait("Category", "Integration")]
[SuppressMessage("FileSystemAbstraction", "JCC9001", Justification = "exe 冒烟测试故意用真实文件系统验证真实 git 进程,非 mock")]
public sealed class InstallGitHubAppCommandExeSmokeTests {
    [Fact]
    public async Task ExeSmoke_MaliciousGitRemote_ShellInjection_RejectedByValidator() {
        var tempDir = CreateTempDir();
        try {
            RunGit(tempDir, "init", "--quiet");
            RunGit(tempDir, "remote", "add", "origin", "https://github.com/foo && start calc/bar.git");

            var cmd = new InstallGitHubAppCommand();
            var result = await cmd.GetCurrentRepoAsync(CancellationToken.None, tempDir);

            result.Should().BeNull("恶意 git remote 'foo && start calc/bar' 解析后应被 GitHubRepoNameValidator 拒绝,不返回注入串");
        } finally {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public async Task ExeSmoke_MaliciousGitRemote_CommandSubstitution_RejectedByValidator() {
        var tempDir = CreateTempDir();
        try {
            RunGit(tempDir, "init", "--quiet");
            RunGit(tempDir, "remote", "add", "origin", "https://github.com/$(whoami)/repo.git");

            var cmd = new InstallGitHubAppCommand();
            var result = await cmd.GetCurrentRepoAsync(CancellationToken.None, tempDir);

            result.Should().BeNull("恶意 git remote '$(whoami)/repo' 解析后应被拒绝");
        } finally {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public async Task ExeSmoke_LegalGitRemote_ReturnsValidatedRepoName() {
        var tempDir = CreateTempDir();
        try {
            RunGit(tempDir, "init", "--quiet");
            RunGit(tempDir, "remote", "add", "origin", "https://github.com/owner/repo.git");

            var cmd = new InstallGitHubAppCommand();
            var result = await cmd.GetCurrentRepoAsync(CancellationToken.None, tempDir);

            result.Should().Be("owner/repo", "合法 git remote 应正常解析为 owner/repo");
        } finally {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public async Task ExeSmoke_LegalSshRemote_ReturnsValidatedRepoName() {
        var tempDir = CreateTempDir();
        try {
            RunGit(tempDir, "init", "--quiet");
            RunGit(tempDir, "remote", "add", "origin", "git@github.com:owner/repo.git");

            var cmd = new InstallGitHubAppCommand();
            var result = await cmd.GetCurrentRepoAsync(CancellationToken.None, tempDir);

            result.Should().Be("owner/repo", "合法 ssh remote 应正常解析为 owner/repo");
        } finally {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public async Task ExeSmoke_NonGitHubRemote_ReturnsNull() {
        var tempDir = CreateTempDir();
        try {
            RunGit(tempDir, "init", "--quiet");
            RunGit(tempDir, "remote", "add", "origin", "https://gitlab.com/owner/repo.git");

            var cmd = new InstallGitHubAppCommand();
            var result = await cmd.GetCurrentRepoAsync(CancellationToken.None, tempDir);

            result.Should().BeNull("非 github.com remote 应返回 null");
        } finally {
            CleanupTempDir(tempDir);
        }
    }

    private static string CreateTempDir() {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "b3-smoke-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        return dir;
    }

    private static void CleanupTempDir(string dir) {
        try { System.IO.Directory.Delete(dir, true); }
        catch (Exception ex) { Console.WriteLine($"清理临时目录失败(可忽略): {ex.Message}"); }
    }

    private static void RunGit(string workingDir, params string[] args) {
        var psi = new System.Diagnostics.ProcessStartInfo {
            FileName = "git",
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) {
            psi.ArgumentList.Add(a);
        }
        using var p = System.Diagnostics.Process.Start(psi) ?? throw new InvalidOperationException("无法启动 git 进程");
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) {
            throw new Xunit.Sdk.XunitException($"git {string.Join(' ', args)} 失败(exit={p.ExitCode}): {stderr}{stdout}");
        }
    }
}
