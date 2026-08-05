namespace Infra.Tests.Process;

[Trait("Category", "Integration")]
public sealed class GitCommandRunnerTests
{
    private readonly GitCommandRunner _runner = new(new PhysicalProcessService(), null);
    private readonly PhysicalFileSystem _fs = new();

    [Fact(Timeout = 15000)]
    public async Task DetectMergeConflictAsync_WithConflict_ReturnsConflictFiles()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"git-conflict-test-{Guid.NewGuid():N}");
        _fs.CreateDirectory(tmp);
        try
        {
            await InitRepoAsync(tmp);

            await _runner.ExecuteAsync("branch feature", tmp);
            await _runner.ExecuteAsync("checkout feature", tmp);
            await _fs.WriteAllTextAsync(Path.Combine(tmp, "file.txt"), "line1\nFEATURE\nline3");
            await _runner.ExecuteAsync("add -A", tmp);
            await _runner.ExecuteAsync("commit -m feature", tmp);
            await _runner.ExecuteAsync("checkout main", tmp);
            await _fs.WriteAllTextAsync(Path.Combine(tmp, "file.txt"), "line1\nMAIN\nline3");
            await _runner.ExecuteAsync("add -A", tmp);
            await _runner.ExecuteAsync("commit -m main", tmp);

            var result = await _runner.DetectMergeConflictAsync("main", "feature", tmp);

            result.HasConflict.Should().BeTrue();
            result.ConflictFiles.Should().Contain("file.txt");
            result.MergedTreeOid.Should().NotBeEmpty();

            var status = await _runner.ExecuteAsync("status --porcelain", tmp);
            status.Output.Should().BeEmpty();
        }
        finally
        {
            try { if (_fs.DirectoryExists(tmp)) _fs.DeleteDirectory(tmp, true); }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"清理临时目录失败: {ex.Message}"); }
        }
    }

    [Fact(Timeout = 15000)]
    public async Task DetectMergeConflictAsync_NoConflict_ReturnsNoConflict()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"git-noconflict-test-{Guid.NewGuid():N}");
        _fs.CreateDirectory(tmp);
        try
        {
            await InitRepoAsync(tmp);

            await _runner.ExecuteAsync("branch feature", tmp);
            await _runner.ExecuteAsync("checkout feature", tmp);
            await _fs.WriteAllTextAsync(Path.Combine(tmp, "other.txt"), "new");
            await _runner.ExecuteAsync("add -A", tmp);
            await _runner.ExecuteAsync("commit -m feature", tmp);
            await _runner.ExecuteAsync("checkout main", tmp);
            await _fs.WriteAllTextAsync(Path.Combine(tmp, "file.txt"), "line1\nMAIN\nline3");
            await _runner.ExecuteAsync("add -A", tmp);
            await _runner.ExecuteAsync("commit -m main", tmp);

            var result = await _runner.DetectMergeConflictAsync("main", "feature", tmp);

            result.HasConflict.Should().BeFalse();
            result.ConflictFiles.Should().BeEmpty();
            result.MergedTreeOid.Should().NotBeEmpty();
        }
        finally
        {
            try { if (_fs.DirectoryExists(tmp)) _fs.DeleteDirectory(tmp, true); }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"清理临时目录失败: {ex.Message}"); }
        }
    }

    private async Task InitRepoAsync(string dir)
    {
        await _runner.ExecuteAsync("init", dir);
        await _runner.ExecuteAsync("config user.email test@test.com", dir);
        await _runner.ExecuteAsync("config user.name test", dir);
        await _fs.WriteAllTextAsync(Path.Combine(dir, "file.txt"), "line1\nline2\nline3");
        await _runner.ExecuteAsync("add -A", dir);
        await _runner.ExecuteAsync("commit -m init", dir);
    }
}
