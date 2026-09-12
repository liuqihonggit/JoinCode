namespace Infra.IO.Tests;

/// <summary>
/// GitWorkspaceResolver 边缘场景测试 — 验证 worktree 解析、路径处理、异常容错。
/// </summary>
public sealed class GitWorkspaceResolverTests
{
    private readonly PhysicalFileSystem _fs = new();

    [Fact]
    public async Task FindGitRoot_MainRepo_DotGitIsDirectory_ReturnsCurrentPath()
    {
        using var tmp = new TempDir(_fs);
        _fs.CreateDirectory(_fs.CombinePath(tmp.Path, ".git"));

        var result = await GitWorkspaceResolver.FindGitRootAsync(tmp.Path, _fs);

        Assert.Equal(tmp.Path, result);
    }

    [Fact]
    public async Task FindGitRoot_Worktree_DotGitIsFile_ResolvesToMainRepo()
    {
        using var mainRepo = new TempDir(_fs);
        using var worktree = new TempDir(_fs);

        var mainGitDir = _fs.CombinePath(mainRepo.Path, ".git");
        _fs.CreateDirectory(mainGitDir);
        _fs.CreateDirectory(_fs.CombinePath(mainGitDir, "worktrees"));
        _fs.CreateDirectory(_fs.CombinePath(mainGitDir, "worktrees", "wt1"));

        var worktreeGitFile = _fs.CombinePath(worktree.Path, ".git");
        var gitdirContent = $"gitdir: {mainGitDir}/worktrees/wt1";
        await _fs.WriteAllTextAsync(worktreeGitFile, gitdirContent);

        var result = await GitWorkspaceResolver.FindGitRootAsync(worktree.Path, _fs);

        Assert.Equal(mainRepo.Path, result);
    }

    [Fact]
    public async Task FindGitRoot_Worktree_InvalidGitdirContent_ReturnsWorktreePath()
    {
        using var worktree = new TempDir(_fs);
        var gitFile = _fs.CombinePath(worktree.Path, ".git");
        await _fs.WriteAllTextAsync(gitFile, "not a gitdir format");

        var result = await GitWorkspaceResolver.FindGitRootAsync(worktree.Path, _fs);

        Assert.Equal(worktree.Path, result);
    }

    [Fact]
    public async Task FindGitRoot_Worktree_EmptyGitFile_ReturnsWorktreePath()
    {
        using var worktree = new TempDir(_fs);
        var gitFile = _fs.CombinePath(worktree.Path, ".git");
        await _fs.WriteAllTextAsync(gitFile, "");

        var result = await GitWorkspaceResolver.FindGitRootAsync(worktree.Path, _fs);

        Assert.Equal(worktree.Path, result);
    }

    [Fact]
    public async Task FindGitRoot_NoGitFound_ReturnsNull()
    {
        using var tmp = new TempDir(_fs);

        var result = await GitWorkspaceResolver.FindGitRootAsync(tmp.Path, _fs);

        Assert.Null(result);
    }

    [Fact]
    public async Task FindGitRoot_StartPathIsFile_TakesDirectory()
    {
        using var tmp = new TempDir(_fs);
        _fs.CreateDirectory(_fs.CombinePath(tmp.Path, ".git"));
        var filePath = _fs.CombinePath(tmp.Path, "test.txt");
        await _fs.WriteAllTextAsync(filePath, "hello");

        var result = await GitWorkspaceResolver.FindGitRootAsync(filePath, _fs);

        Assert.Equal(tmp.Path, result);
    }

    [Fact]
    public async Task FindGitRoot_StartPathIsNull_UsesCurrentDirectory()
    {
        var result = await GitWorkspaceResolver.FindGitRootAsync(null!, _fs);

        if (result is not null)
        {
            var gitPath = _fs.CombinePath(result, ".git");
            Assert.True(_fs.DirectoryExists(gitPath) || _fs.FileExists(gitPath),
                $"FindGitRootAsync returned '{result}' but it has no .git");
        }
    }

    [Fact]
    public void FindSolutionRoot_HasSln_ReturnsDirectory()
    {
        using var tmp = new TempDir(_fs);
        var slnPath = _fs.CombinePath(tmp.Path, "test.sln");
        _fs.WriteAllText(slnPath, "fake sln content");

        var result = GitWorkspaceResolver.FindSolutionRoot(tmp.Path, _fs);

        Assert.Equal(tmp.Path, result);
    }

    [Fact]
    public void FindSolutionRoot_HasSlnx_ReturnsDirectory()
    {
        using var tmp = new TempDir(_fs);
        var slnxPath = _fs.CombinePath(tmp.Path, "test.slnx");
        _fs.WriteAllText(slnxPath, "<Solution/>");

        var result = GitWorkspaceResolver.FindSolutionRoot(tmp.Path, _fs);

        Assert.Equal(tmp.Path, result);
    }

    [Fact]
    public void FindSolutionRoot_NoSolution_ReturnsNull()
    {
        using var tmp = new TempDir(_fs);

        var result = GitWorkspaceResolver.FindSolutionRoot(tmp.Path, _fs);

        Assert.Null(result);
    }

    [Fact]
    public async Task FindWorkspaceRoot_SlnFound_PrioritizesOverGit()
    {
        using var tmp = new TempDir(_fs);
        _fs.WriteAllText(_fs.CombinePath(tmp.Path, "test.sln"), "fake");
        _fs.CreateDirectory(_fs.CombinePath(tmp.Path, ".git"));

        var result = await GitWorkspaceResolver.FindWorkspaceRootAsync(tmp.Path, _fs);

        Assert.Equal(tmp.Path, result);
    }

    [Fact]
    public async Task FindWorkspaceRoot_NoSln_FallsBackToGitRoot()
    {
        using var mainRepo = new TempDir(_fs);
        using var subDir = new TempDir(_fs, mainRepo.Path);
        _fs.CreateDirectory(_fs.CombinePath(mainRepo.Path, ".git"));

        var result = await GitWorkspaceResolver.FindWorkspaceRootAsync(subDir.Path, _fs);

        Assert.Equal(mainRepo.Path, result);
    }

    [Fact]
    public void FindGitWorkspaceDir_DotGitIsFile_ReturnsCurrentPath()
    {
        using var worktree = new TempDir(_fs);
        _fs.WriteAllText(_fs.CombinePath(worktree.Path, ".git"), "gitdir: /fake");

        var result = GitWorkspaceResolver.FindGitWorkspaceDir(worktree.Path, _fs);

        Assert.Equal(worktree.Path, result);
    }

    [Fact]
    public void FindGitWorkspaceDir_DotGitIsDirectory_ReturnsCurrentPath()
    {
        using var tmp = new TempDir(_fs);
        _fs.CreateDirectory(_fs.CombinePath(tmp.Path, ".git"));

        var result = GitWorkspaceResolver.FindGitWorkspaceDir(tmp.Path, _fs);

        Assert.Equal(tmp.Path, result);
    }

    [Fact]
    public void FindGitWorkspaceDir_NoGit_ReturnsNull()
    {
        using var tmp = new TempDir(_fs);

        var result = GitWorkspaceResolver.FindGitWorkspaceDir(tmp.Path, _fs);

        Assert.Null(result);
    }

    [Fact]
    public async Task FindGitRoot_Worktree_GitdirPointsToNonExistentPath_ReturnsWorktreePath()
    {
        using var worktree = new TempDir(_fs);
        var gitFile = _fs.CombinePath(worktree.Path, ".git");
        await _fs.WriteAllTextAsync(gitFile, "gitdir: /nonexistent/path/.git/worktrees/wt1");

        var result = await GitWorkspaceResolver.FindGitRootAsync(worktree.Path, _fs);

        Assert.Equal(worktree.Path, result);
    }

    [Fact]
    public async Task FindGitRoot_NestedDirectory_FindsParentGitRoot()
    {
        using var tmp = new TempDir(_fs);
        _fs.CreateDirectory(_fs.CombinePath(tmp.Path, ".git"));
        var nested = _fs.CombinePath(tmp.Path, "a", "b", "c");
        _fs.CreateDirectory(nested);

        var result = await GitWorkspaceResolver.FindGitRootAsync(nested, _fs);

        Assert.Equal(tmp.Path, result);
    }

    private sealed class TempDir : IDisposable
    {
        private readonly PhysicalFileSystem _fs;
        public string Path { get; }

        public TempDir(PhysicalFileSystem fs, string? parent = null)
        {
            _fs = fs;
            Path = System.IO.Path.Combine(parent ?? System.IO.Path.GetTempPath(), $"gwr_test_{Guid.NewGuid():N}");
            _fs.CreateDirectory(Path);
        }

        public void Dispose()
        {
            try { _fs.DeleteDirectory(Path, recursive: true); }
            catch (IOException ex) { Console.WriteLine($"TempDir cleanup failed: {ex.Message}"); }
        }
    }
}
