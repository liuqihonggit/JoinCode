namespace Infra.IO.Tests;

/// <summary>
/// FileWatcherActorBase 单元测试 — 验证事件→命令转换、防回声、自定义命令扩展。
/// </summary>
public class FileWatcherActorBaseTests
{
    [Fact]
    public async Task StartWatcher_FileChanged_HandleFileChangedAsyncInvoked()
    {
        var fs = new InMemoryFileSystem();
        var dir = "/test";
        fs.CreateDirectory(dir);
        var actor = new TestFileWatcherActor(fs);
        await actor.SendAsync(new FileWatcherStartCmd(dir, "*.txt", TimeSpan.FromMilliseconds(50)));

        fs.WriteAllText($"{dir}/a.txt", "hello");
        await Task.Delay(300);

        var changes = actor.GetChanges();
        changes.Any(c => c.FilePath.EndsWith("a.txt")).Should().BeTrue();
        await actor.DisposeAsync();
    }

    [Fact]
    public async Task MarkInternalWrite_FileChanged_Filtered()
    {
        var fs = new InMemoryFileSystem();
        var dir = "/test";
        fs.CreateDirectory(dir);
        var actor = new TestFileWatcherActor(fs);
        await actor.SendAsync(new FileWatcherStartCmd(dir, "*.txt", TimeSpan.FromMilliseconds(50)));

        actor.MarkInternalWrite($"{dir}/b.txt");
        fs.WriteAllText($"{dir}/b.txt", "data");
        await Task.Delay(300);

        var changes = actor.GetChanges();
        changes.Any(c => c.FilePath.EndsWith("b.txt")).Should().BeFalse();
        await actor.DisposeAsync();
    }

    [Fact]
    public async Task CustomCommand_HandleCustomCommandAsyncInvoked()
    {
        var fs = new InMemoryFileSystem();
        var actor = new TestFileWatcherActor(fs);
        await actor.SendAsync(new TestCustomCmd("test-data"));

        await Task.Delay(100);
        actor.CustomCommands.Should().Contain("test-data");
        await actor.DisposeAsync();
    }

    [Fact]
    public async Task StopWatcher_NoMoreEvents()
    {
        var fs = new InMemoryFileSystem();
        var dir = "/test";
        fs.CreateDirectory(dir);
        var actor = new TestFileWatcherActor(fs);
        await actor.SendAsync(new FileWatcherStartCmd(dir, "*.txt", TimeSpan.FromMilliseconds(50)));
        await actor.SendAsync(new FileWatcherStopCmd());
        await Task.Delay(100);

        fs.WriteAllText($"{dir}/c.txt", "after-stop");
        await Task.Delay(300);

        var changes = actor.GetChanges();
        changes.Any(c => c.FilePath.EndsWith("c.txt")).Should().BeFalse();
        await actor.DisposeAsync();
    }

    private sealed class TestFileWatcherActor : FileWatcherActorBase
    {
        private readonly List<FileChangedCmd> _changes = new();
        private readonly List<string> _customCommands = new();

        public TestFileWatcherActor(IFileSystem fs) : base(fs, 100) { }

        protected override ValueTask HandleFileChangedAsync(string filePath, WatcherChangeTypes kind, DateTimeOffset timestamp, CancellationToken ct)
        {
            lock (_changes) _changes.Add(new FileChangedCmd(filePath, kind, timestamp));
            return ValueTask.CompletedTask;
        }

        protected override ValueTask HandleCustomCommandAsync(FileWatcherCommand cmd, CancellationToken ct)
        {
            if (cmd is TestCustomCmd c)
                lock (_customCommands) _customCommands.Add(c.Data);
            return ValueTask.CompletedTask;
        }

        public List<FileChangedCmd> GetChanges() { lock (_changes) return _changes.ToList(); }
        public List<string> CustomCommands { get { lock (_customCommands) return _customCommands.ToList(); } }
    }

    private sealed record TestCustomCmd(string Data) : FileWatcherCommand;
}
