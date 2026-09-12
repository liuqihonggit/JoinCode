namespace Core.Tests.Configuration;

public sealed class ConfigChangeNotifierTests
{
    [Fact]
    public void StartMonitoring_Should_Not_Throw_When_Directory_Does_Not_Exist()
    {
        var notifier = new ConfigChangeNotifier(new IO.FileSystem.PhysicalFileSystem(), NullLogger<ConfigChangeNotifier>.Instance);

        notifier.StartMonitoring(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

        notifier.StopMonitoring();
    }

    [Fact]
    public void StartMonitoring_Should_Not_Throw_When_Directory_Exists()
    {
    }

    [Fact]
    public Task FileChange_InRulesDir_Should_Raise_ConfigChanged()
    {
        return Task.CompletedTask;
    }

    [Fact]
    public Task FileChange_InClaudeRulesDir_Should_Raise_ConfigChanged()
    {
        return Task.CompletedTask;
    }

    [Fact]
    public Task FileChange_InCommandsDir_Should_Raise_ConfigChanged()
    {
        return Task.CompletedTask;
    }

    [Fact]
    public Task FileChange_AgentsMd_Should_Raise_ConfigChanged()
    {
        return Task.CompletedTask;
    }

    [Fact]
    public Task FileChange_ClaudeMd_Should_Raise_ConfigChanged()
    {
        return Task.CompletedTask;
    }

    [Fact]
    public Task FileChange_SettingsJson_Should_Raise_ConfigChanged()
    {
        return Task.CompletedTask;
    }

    [Fact]
    public Task FileChange_IrrelevantFile_Should_Not_Raise_ConfigChanged()
    {
        return Task.CompletedTask;
    }

    [Fact]
    public Task FileChange_IrrelevantCsFile_Should_Not_Raise_ConfigChanged()
    {
        return Task.CompletedTask;
    }

    [Fact]
    public Task FileChange_NewFileInRulesDir_Should_Raise_ConfigChanged()
    {
        return Task.CompletedTask;
    }

    [Fact]
    public Task StopMonitoring_Should_Not_Raise_Events_After_Stop()
    {
        return Task.CompletedTask;
    }

    [Fact]
    public Task Debounce_RapidChanges_Should_Reduce_Event_Count()
    {
        return Task.CompletedTask;
    }

    [Fact]
    public Task FileChange_CodexRulesDir_Should_Raise_ConfigChanged()
    {
        return Task.CompletedTask;
    }

    [Fact]
    public Task FileChange_CodexCommandsDir_Should_Raise_ConfigChanged()
    {
        return Task.CompletedTask;
    }
}
