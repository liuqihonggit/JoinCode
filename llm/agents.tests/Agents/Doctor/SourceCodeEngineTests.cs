namespace Core.Tests.Agents.Doctor;


public class SourceCodeEngineTests
{
    [Fact]
    public async Task LocateSourceRepository_WithExplicitHintPath_ReturnsLocation()
    {
        var fs = CreateFileSystemWithGitRoot(out var gitRoot);
        var engine = CreateEngine(fs);

        var result = await engine.LocateSourceRepositoryAsync(hintPath: gitRoot);

        Assert.True(result.IsAvailable);
        Assert.Equal(gitRoot, result.GitRoot);
    }

    [Fact]
    public async Task LocateSourceRepository_NoGitRoot_ReturnsNotAvailable()
    {
        var fs = new InMemoryFileSystem();
        fs.CreateDirectory("/tmp/nogit");
        fs.SetCurrentDirectory("/tmp/nogit");
        var engine = CreateEngine(fs);

        var result = await engine.LocateSourceRepositoryAsync(hintPath: "/tmp/nogit");

        Assert.False(result.IsAvailable);
        Assert.NotNull(result.FailureReason);
        Assert.NotEmpty(result.FailureReason);
    }

    [Fact]
    public async Task LocateSourceRepository_WithEnvVar_ReturnsLocation()
    {
        var fs = CreateFileSystemWithGitRoot(out var gitRoot);
        var engine = CreateEngine(fs);
        using var env = new EnvVarScope("JCC_SOURCE_DIR", gitRoot);

        var result = await engine.LocateSourceRepositoryAsync();

        Assert.True(result.IsAvailable);
        Assert.Equal(gitRoot, result.GitRoot);
    }

    [Fact]
    public async Task BuildFullProject_AllSlnxMissing_ReturnsFailed()
    {
        var fs = new InMemoryFileSystem();
        fs.CreateDirectory("/tmp/empty");
        var engine = CreateEngine(fs);

        var result = await engine.BuildFullProjectAsync("/tmp/empty", "Debug");

        Assert.False(result.Success);
        Assert.NotEmpty(result.LayerResults);
        Assert.Equal(1, result.FirstFailedLayer);
    }

    [Fact]
    public async Task GetArtifactExePath_Debug_ReturnsCorrectPath()
    {
        var fs = new InMemoryFileSystem();
        var engine = CreateEngine(fs);

        var path = await engine.GetArtifactExePathAsync("/project/w2", "Debug");

        Assert.Contains("artifacts", path);
        Assert.Contains("JoinCode", path);
        Assert.Contains("Debug", path);
        Assert.EndsWith("jcc.exe", path);
    }

    [Fact]
    public async Task GetArtifactExePath_Release_ReturnsCorrectPath()
    {
        var fs = new InMemoryFileSystem();
        var engine = CreateEngine(fs);

        var path = await engine.GetArtifactExePathAsync("/project/w2", "Release");

        Assert.Contains("Release", path);
        Assert.EndsWith("jcc.exe", path);
    }

    [Fact]
    public async Task SwapExe_NewExeNotExists_ReturnsFailure()
    {
        var fs = new InMemoryFileSystem();
        var engine = CreateEngine(fs);

        var result = await engine.SwapExeAsync("/old/jcc.exe", "/new/jcc.exe", "patient-1");

        Assert.False(result.Success);
        Assert.Contains("不存在", result.Description);
    }

    [Fact]
    public async Task SwapExe_NewExeExists_CopiesAndSucceeds()
    {
        var fs = new InMemoryFileSystem();
        fs.WriteAllText("/old/jcc.exe", "old-content");
        fs.WriteAllText("/new/jcc.exe", "new-content");
        var engine = CreateEngine(fs);

        var result = await engine.SwapExeAsync("/old/jcc.exe", "/new/jcc.exe", "patient-1");

        Assert.True(result.Success);
        Assert.Equal("/old/jcc.exe", result.OldExePath);
        Assert.Equal("/new/jcc.exe", result.NewExePath);
    }

    [Fact]
    public void FullBuildResult_FirstFailedLayer_ReturnsNullWhenAllSuccess()
    {
        var result = new FullBuildResult
        {
            Success = true,
            LayerResults =
            [
                new SlnxBuildResult { Layer = 1, SlnxName = "Generators.slnx", Success = true },
                new SlnxBuildResult { Layer = 2, SlnxName = "Foundation.slnx", Success = true },
            ]
        };

        Assert.Null(result.FirstFailedLayer);
    }

    [Fact]
    public void FullBuildResult_FirstFailedLayer_ReturnsLayerWhenFailed()
    {
        var result = new FullBuildResult
        {
            Success = false,
            LayerResults =
            [
                new SlnxBuildResult { Layer = 1, SlnxName = "Generators.slnx", Success = true },
                new SlnxBuildResult { Layer = 2, SlnxName = "Foundation.slnx", Success = false, ExitCode = 1 },
            ]
        };

        Assert.Equal(2, result.FirstFailedLayer);
    }

    private static InMemoryFileSystem CreateFileSystemWithGitRoot(out string gitRoot)
    {
        var fs = new InMemoryFileSystem();
        gitRoot = "/project/w2";
        fs.CreateDirectory(gitRoot);
        fs.CreateDirectory(Path.Combine(gitRoot, ".git"));
        fs.SetCurrentDirectory(gitRoot);
        return fs;
    }

    private static SourceCodeEngine CreateEngine(IFileSystem fs)
    {
        return new SourceCodeEngine(fs, new StubGitCommandRunner());
    }

    private sealed class EnvVarScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _oldValue;

        public EnvVarScope(string name, string value)
        {
            _name = name;
            _oldValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(_name, _oldValue);
        }
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
