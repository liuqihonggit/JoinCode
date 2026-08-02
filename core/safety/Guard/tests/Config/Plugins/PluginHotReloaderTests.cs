namespace Core.Tests.Plugins;

public sealed class PluginHotReloaderTests
{
    private readonly Mock<IPluginManager> _pluginManager;
    private readonly Mock<IFileSystem> _fileSystem;
    private readonly PluginHotReloader _reloader;
    private static readonly IFileSystem PhysicalFs = new PhysicalFileSystem();

    public PluginHotReloaderTests()
    {
        _pluginManager = new Mock<IPluginManager>();
        _fileSystem = new Mock<IFileSystem>();
        _fileSystem.Setup(fs => fs.Watch(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string path, string filter) => new PhysicalFileSystemWatcher(path, filter));
        _reloader = new PluginHotReloader(_pluginManager.Object, _fileSystem.Object, NullLogger<PluginHotReloader>.Instance);
    }

    [Fact]
    public void Constructor_NullPluginManager_ShouldThrow()
    {
        var act = () => new PluginHotReloader(null!, _fileSystem.Object, NullLogger<PluginHotReloader>.Instance);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void IsWatching_Initially_ShouldBeFalse()
    {
        _reloader.IsWatching.Should().BeFalse();
    }

    [Fact]
    public async Task StartWatchingAsync_NonExistentDirectory_ShouldThrow()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        _fileSystem.Setup(fs => fs.DirectoryExists("/nonexistent/path")).Returns(false);
        var act = async () => await _reloader.StartWatchingAsync("/nonexistent/path", cts.Token).ConfigureAwait(true);
        await act.Should().ThrowAsync<DirectoryNotFoundException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task StartWatchingAsync_NullOrWhiteSpace_ShouldThrow()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var act = async () => await _reloader.StartWatchingAsync(null!, cts.Token).ConfigureAwait(true);
        await act.Should().ThrowAsync<ArgumentException>().ConfigureAwait(true);

        var act2 = async () => await _reloader.StartWatchingAsync("  ", cts.Token).ConfigureAwait(true);
        await act2.Should().ThrowAsync<ArgumentException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task StartWatchingAsync_ValidDirectory_ShouldSetIsWatchingTrue()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var tempDir = CreateRealTempDir();
        _fileSystem.Setup(fs => fs.DirectoryExists(tempDir)).Returns(true);
        try
        {
            await _reloader.StartWatchingAsync(tempDir, cts.Token).ConfigureAwait(true);
            _reloader.IsWatching.Should().BeTrue();
        }
        finally
        {
            await _reloader.StopWatchingAsync(cts.Token).ConfigureAwait(true);
            CleanupRealTempDir(tempDir);
        }
    }

    [Fact]
    public async Task StopWatchingAsync_WhenWatching_ShouldSetIsWatchingFalse()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var tempDir = CreateRealTempDir();
        _fileSystem.Setup(fs => fs.DirectoryExists(tempDir)).Returns(true);
        try
        {
            await _reloader.StartWatchingAsync(tempDir, cts.Token).ConfigureAwait(true);
            await _reloader.StopWatchingAsync(cts.Token).ConfigureAwait(true);
            _reloader.IsWatching.Should().BeFalse();
        }
        finally
        {
            if (_reloader.IsWatching)
            {
                await _reloader.StopWatchingAsync(cts.Token).ConfigureAwait(true);
            }
            CleanupRealTempDir(tempDir);
        }
    }

    [Fact]
    public async Task StopWatchingAsync_WhenNotWatching_ShouldNotThrow()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var act = async () => await _reloader.StopWatchingAsync(cts.Token).ConfigureAwait(true);
        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task StartWatchingAsync_AlreadyWatching_ShouldNotThrow()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var tempDir = CreateRealTempDir();
        _fileSystem.Setup(fs => fs.DirectoryExists(tempDir)).Returns(true);
        try
        {
            await _reloader.StartWatchingAsync(tempDir, cts.Token).ConfigureAwait(true);
            var act = async () => await _reloader.StartWatchingAsync(tempDir, cts.Token).ConfigureAwait(true);
            await act.Should().NotThrowAsync().ConfigureAwait(true);
        }
        finally
        {
            await _reloader.StopWatchingAsync(cts.Token).ConfigureAwait(true);
            CleanupRealTempDir(tempDir);
        }
    }

    [Fact]
    public async Task DisposeAsync_ShouldStopWatching()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var tempDir = CreateRealTempDir();
        _fileSystem.Setup(fs => fs.DirectoryExists(tempDir)).Returns(true);
        try
        {
            await _reloader.StartWatchingAsync(tempDir, cts.Token).ConfigureAwait(true);
            await _reloader.DisposeAsync().ConfigureAwait(true);
            _reloader.IsWatching.Should().BeFalse();
        }
        finally
        {
            if (_reloader.IsWatching)
            {
                await _reloader.StopWatchingAsync(cts.Token).ConfigureAwait(true);
            }
            CleanupRealTempDir(tempDir);
        }
    }

    private static string CreateRealTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), $"plugin-test-{Guid.NewGuid():N}");
        PhysicalFs.CreateDirectory(path);
        return path;
    }

    private static void CleanupRealTempDir(string path)
    {
        try
        {
            if (PhysicalFs.DirectoryExists(path))
                PhysicalFs.DeleteDirectory(path, recursive: true);
        }
        catch (Exception ex)
        {
            // 最佳努力清理
            System.Diagnostics.Trace.WriteLine($"Temp directory cleanup failed for path '{path}': {ex.Message}");
        }
    }
}
