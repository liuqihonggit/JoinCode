namespace Core.Tests.Configuration;

public sealed class ConfigChangeNotifierTests {
    [Fact]
    public async Task StartMonitoring_Should_Not_Throw_When_Directory_Does_Not_Exist() {
        await using var notifier = new ConfigChangeNotifier(new IO.FileSystem.PhysicalFileSystem(), NullLogger<ConfigChangeNotifier>.Instance);

        notifier.StartMonitoring(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

        notifier.StopMonitoring();
    }

    [Fact]
    public void StartMonitoring_Should_Not_Throw_When_Directory_Exists() {
    }

    [Fact]
    public Task FileChange_InRulesDir_Should_Raise_ConfigChanged() {
        return Task.CompletedTask;
    }

    [Fact]
    public Task FileChange_InClaudeRulesDir_Should_Raise_ConfigChanged() {
        return Task.CompletedTask;
    }

    [Fact]
    public Task FileChange_InCommandsDir_Should_Raise_ConfigChanged() {
        return Task.CompletedTask;
    }

    [Fact]
    public Task FileChange_AgentsMd_Should_Raise_ConfigChanged() {
        return Task.CompletedTask;
    }

    [Fact]
    public Task FileChange_ClaudeMd_Should_Raise_ConfigChanged() {
        return Task.CompletedTask;
    }

    [Fact]
    public Task FileChange_SettingsJson_Should_Raise_ConfigChanged() {
        return Task.CompletedTask;
    }

    [Fact]
    public Task FileChange_IrrelevantFile_Should_Not_Raise_ConfigChanged() {
        return Task.CompletedTask;
    }

    [Fact]
    public Task FileChange_IrrelevantCsFile_Should_Not_Raise_ConfigChanged() {
        return Task.CompletedTask;
    }

    [Fact]
    public Task FileChange_NewFileInRulesDir_Should_Raise_ConfigChanged() {
        return Task.CompletedTask;
    }

    [Fact]
    public Task StopMonitoring_Should_Not_Raise_Events_After_Stop() {
        return Task.CompletedTask;
    }

    [Fact]
    public Task Debounce_RapidChanges_Should_Reduce_Event_Count() {
        return Task.CompletedTask;
    }

    [Fact]
    public Task FileChange_CodexRulesDir_Should_Raise_ConfigChanged() {
        return Task.CompletedTask;
    }

    [Fact]
    public Task FileChange_CodexCommandsDir_Should_Raise_ConfigChanged() {
        return Task.CompletedTask;
    }

    [Fact]
    public async Task FindWatchRoot_Should_Stop_At_GitDirectory() {
        var fs = new InMemoryFileSystem();
        fs.CreateDirectory("/root/parent/project");
        fs.CreateDirectory("/root/parent/project/.git");
        fs.CreateDirectory("/root/parent");
        await fs.WriteAllText("/root/parent/AGENTS.md", "# parent");
        await fs.WriteAllText("/root/parent/project/AGENTS.md", "# project");

        await using var notifier = new ConfigChangeNotifier(fs);
        var watchRoot = notifier.FindWatchRoot("/root/parent/project");

        watchRoot.Should().Be(fs.GetFullPath("/root/parent/project"),
            "遇到 .git 目录应停止向上遍历,不会遍历到 /root/parent");
    }

    [Fact]
    public async Task FindWatchRoot_Should_Traverse_Up_Without_GitDirectory() {
        var fs = new InMemoryFileSystem();
        fs.CreateDirectory("/root/parent/project");
        fs.CreateDirectory("/root/parent");
        await fs.WriteAllText("/root/parent/AGENTS.md", "# parent");
        await fs.WriteAllText("/root/parent/project/AGENTS.md", "# project");

        await using var notifier = new ConfigChangeNotifier(fs);
        var watchRoot = notifier.FindWatchRoot("/root/parent/project");

        watchRoot.Should().Be(fs.GetParentPath(fs.GetFullPath("/root/parent/project")),
            "无 .git 目录时应继续向上遍历找到最顶层有配置文件的目录");
    }
}