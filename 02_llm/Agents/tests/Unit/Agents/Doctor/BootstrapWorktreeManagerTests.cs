namespace Core.Tests.Agents.Doctor;


[Trait("Category", "Integration")]
public class BootstrapWorktreeManagerTests
{
    [Fact]
    public async Task CreateAsync_GeneratesBranchNameWithTimestamp()
    {
        var manager = CreateManager(out var executedCommands);

        var result = await manager.CreateAsync("/project/w2");

        Assert.Contains("/project/w2", result.WorktreePath);
        Assert.StartsWith("doctor-bootstrap-", result.BranchName);
        Assert.Equal("/project/w2", result.GitRoot);
    }

    [Fact]
    public async Task CreateAsync_WithBaseRef_UsesBaseRef()
    {
        var manager = CreateManager(out _);

        var result = await manager.CreateAsync("/project/w2", baseRef: "main");

        Assert.Equal("main", result.BaseRef);
    }

    [Fact]
    public async Task CreateAsync_WithoutBaseRef_DefaultsToHEAD()
    {
        var manager = CreateManager(out _);

        var result = await manager.CreateAsync("/project/w2");

        Assert.Equal("HEAD", result.BaseRef);
    }

    [Fact]
    public async Task GetCurrentAsync_AfterCreate_ReturnsWorktree()
    {
        var manager = CreateManager(out _);

        await manager.CreateAsync("/project/w2");
        var current = await manager.GetCurrentAsync();

        Assert.NotNull(current);
        Assert.Contains("/project/w2", current.WorktreePath);
    }

    [Fact]
    public async Task GetCurrentAsync_BeforeCreate_ReturnsNull()
    {
        var manager = CreateManager(out _);

        var current = await manager.GetCurrentAsync();

        Assert.Null(current);
    }

    [Fact]
    public async Task CleanupAsync_AfterCreate_ClearsCurrent()
    {
        var manager = CreateManager(out _);

        await manager.CreateAsync("/project/w2");
        await manager.CleanupAsync();
        var current = await manager.GetCurrentAsync();

        Assert.Null(current);
    }

    private static BootstrapWorktreeManager CreateManager(out List<string> executedCommands)
    {
        executedCommands = [];
        var fs = new InMemoryFileSystem();
        var gitRunner = new StubGitCommandRunner();
        return new BootstrapWorktreeManager(fs, gitRunner);
    }

    private sealed class StubGitCommandRunner : IGitCommandRunner
    {
        public Task<GitCommandResult> ExecuteAsync(string arguments, string? workingDirectory = null, CancellationToken ct = default)
            => Task.FromResult(new GitCommandResult { Success = true, Output = string.Empty, Error = string.Empty, ExitCode = 0 });

        public Task<MergeConflictResult> DetectMergeConflictAsync(string branch1, string branch2, string? workingDirectory = null, CancellationToken ct = default)
            => Task.FromResult(new MergeConflictResult { HasConflict = false });

        public Task<StaleConflictMarkerResult> DetectStaleConflictMarkersAsync(string? workingDirectory = null, CancellationToken ct = default)
            => Task.FromResult(new StaleConflictMarkerResult { HasStaleMarkers = false });
    }
}
