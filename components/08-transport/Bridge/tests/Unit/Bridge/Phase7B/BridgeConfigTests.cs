
namespace Bridge.Tests.Phase7B;

public sealed class BridgeConfigTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var config = new BridgeConfig();

        Assert.False(config.Enabled);
        Assert.Equal(5, config.MaxSessions);
        Assert.Equal(BridgeSpawnMode.SingleSession, config.SpawnMode);
        Assert.False(config.Verbose);
        Assert.False(config.Sandbox);
        Assert.Equal("bridge", config.WorkerType);
        Assert.Equal(30, config.SessionTimeoutMinutes);
        Assert.NotEmpty(config.BridgeId);
    }

    [Fact]
    public void ClientFields_DefaultValues()
    {
        var config = new BridgeConfig();

        Assert.Equal(Environment.MachineName, config.MachineName);
        Assert.Empty(config.Dir);
        Assert.Empty(config.Branch);
        Assert.Null(config.GitRepoUrl);
        Assert.Null(config.ReuseEnvironmentId);
        Assert.Null(config.DebugFile);
        Assert.Equal(0, config.SessionTimeoutMs);
    }

    [Fact]
    public void BridgeSpawnMode_EnumValues()
    {
        Assert.Equal("single-session", BridgeSpawnMode.SingleSession.ToValue());
        Assert.Equal("worktree", BridgeSpawnMode.Worktree.ToValue());
        Assert.Equal("same-dir", BridgeSpawnMode.SameDir.ToValue());
    }
}
