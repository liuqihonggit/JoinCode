
namespace Bridge.Tests.Phase7D;

public sealed class BridgeMainArgsTests
{
    #region Parse — 基本参数

    [Fact]
    public void Parse_EmptyArgs_ReturnsDefaults()
    {
        var result = BridgeMainArgsParser.Parse([]);

        Assert.False(result.Verbose);
        Assert.False(result.Sandbox);
        Assert.Null(result.DebugFile);
        Assert.Null(result.SessionTimeoutMs);
        Assert.Null(result.PermissionMode);
        Assert.Null(result.Name);
        Assert.Null(result.SpawnMode);
        Assert.Null(result.Capacity);
        Assert.Null(result.CreateSessionInDir);
        Assert.Null(result.SessionId);
        Assert.False(result.ContinueSession);
        Assert.False(result.Help);
        Assert.Null(result.Error);
        Assert.False(result.HasError);
    }

    [Fact]
    public void Parse_Verbose_ShortFlag()
    {
        var result = BridgeMainArgsParser.Parse(["-v"]);
        Assert.True(result.Verbose);
    }

    [Fact]
    public void Parse_Verbose_LongFlag()
    {
        var result = BridgeMainArgsParser.Parse(["--verbose"]);
        Assert.True(result.Verbose);
    }

    [Fact]
    public void Parse_Sandbox_Enabled()
    {
        var result = BridgeMainArgsParser.Parse(["--sandbox"]);
        Assert.True(result.Sandbox);
    }

    [Fact]
    public void Parse_Sandbox_Disabled()
    {
        var result = BridgeMainArgsParser.Parse(["--no-sandbox"]);
        Assert.False(result.Sandbox);
    }

    [Fact]
    public void Parse_DebugFile()
    {
        var result = BridgeMainArgsParser.Parse(["--debug-file", "/tmp/bridge.log"]);
        Assert.Equal("/tmp/bridge.log", result.DebugFile);
    }

    [Fact]
    public void Parse_DebugFile_MissingValue_ReturnsError()
    {
        var result = BridgeMainArgsParser.Parse(["--debug-file"]);
        Assert.True(result.HasError);
        Assert.Contains("debug-file", result.Error!);
    }

    [Fact]
    public void Parse_SessionTimeout()
    {
        var result = BridgeMainArgsParser.Parse(["--session-timeout", "3600"]);
        Assert.Equal(3600000, result.SessionTimeoutMs);
    }

    [Fact]
    public void Parse_SessionTimeout_InvalidValue_ReturnsError()
    {
        var result = BridgeMainArgsParser.Parse(["--session-timeout", "abc"]);
        Assert.True(result.HasError);
    }

    [Fact]
    public void Parse_SessionTimeout_Zero_ReturnsError()
    {
        var result = BridgeMainArgsParser.Parse(["--session-timeout", "0"]);
        Assert.True(result.HasError);
    }

    [Fact]
    public void Parse_SessionTimeout_MissingValue_ReturnsError()
    {
        var result = BridgeMainArgsParser.Parse(["--session-timeout"]);
        Assert.True(result.HasError);
    }

    [Fact]
    public void Parse_PermissionMode()
    {
        var result = BridgeMainArgsParser.Parse(["--permission-mode", "auto-accept"]);
        Assert.Equal("auto-accept", result.PermissionMode);
    }

    [Fact]
    public void Parse_PermissionMode_MissingValue_ReturnsError()
    {
        var result = BridgeMainArgsParser.Parse(["--permission-mode"]);
        Assert.True(result.HasError);
    }

    [Fact]
    public void Parse_Name()
    {
        var result = BridgeMainArgsParser.Parse(["--name", "my-bridge"]);
        Assert.Equal("my-bridge", result.Name);
    }

    [Fact]
    public void Parse_Name_MissingValue_ReturnsError()
    {
        var result = BridgeMainArgsParser.Parse(["--name"]);
        Assert.True(result.HasError);
    }

    #endregion

    #region Parse — SpawnMode

    [Fact]
    public void Parse_SpawnSession()
    {
        var result = BridgeMainArgsParser.Parse(["--spawn", "session"]);
        Assert.Equal(BridgeSpawnMode.SingleSession, result.SpawnMode);
    }

    [Fact]
    public void Parse_SpawnSameDir()
    {
        var result = BridgeMainArgsParser.Parse(["--spawn", "same-dir"]);
        Assert.Equal(BridgeSpawnMode.SameDir, result.SpawnMode);
    }

    [Fact]
    public void Parse_SpawnWorktree()
    {
        var result = BridgeMainArgsParser.Parse(["--spawn", "worktree"]);
        Assert.Equal(BridgeSpawnMode.Worktree, result.SpawnMode);
    }

    [Fact]
    public void Parse_SpawnInvalid_ReturnsError()
    {
        var result = BridgeMainArgsParser.Parse(["--spawn", "invalid"]);
        Assert.True(result.HasError);
        Assert.Contains("spawn", result.Error!);
    }

    [Fact]
    public void Parse_SpawnMissingValue_ReturnsError()
    {
        var result = BridgeMainArgsParser.Parse(["--spawn"]);
        Assert.True(result.HasError);
    }

    #endregion

    #region Parse — Capacity / CreateSessionInDir

    [Fact]
    public void Parse_Capacity()
    {
        var result = BridgeMainArgsParser.Parse(["--capacity", "5"]);
        Assert.Equal(5, result.Capacity);
    }

    [Fact]
    public void Parse_Capacity_InvalidValue_ReturnsError()
    {
        var result = BridgeMainArgsParser.Parse(["--capacity", "abc"]);
        Assert.True(result.HasError);
    }

    [Fact]
    public void Parse_Capacity_Zero_ReturnsError()
    {
        var result = BridgeMainArgsParser.Parse(["--capacity", "0"]);
        Assert.True(result.HasError);
    }

    [Fact]
    public void Parse_CreateSessionInDir()
    {
        var result = BridgeMainArgsParser.Parse(["--create-session-in-dir"]);
        Assert.True(result.CreateSessionInDir);
    }

    [Fact]
    public void Parse_NoCreateSessionInDir()
    {
        var result = BridgeMainArgsParser.Parse(["--no-create-session-in-dir"]);
        Assert.False(result.CreateSessionInDir);
    }

    #endregion

    #region Parse — SessionId / Continue

    [Fact]
    public void Parse_SessionId()
    {
        var result = BridgeMainArgsParser.Parse(["--session-id", "cse_123"]);
        Assert.Equal("cse_123", result.SessionId);
    }

    [Fact]
    public void Parse_SessionId_MissingValue_ReturnsError()
    {
        var result = BridgeMainArgsParser.Parse(["--session-id"]);
        Assert.True(result.HasError);
    }

    [Fact]
    public void Parse_Continue_ShortFlag()
    {
        var result = BridgeMainArgsParser.Parse(["-c"]);
        Assert.True(result.ContinueSession);
    }

    [Fact]
    public void Parse_Continue_LongFlag()
    {
        var result = BridgeMainArgsParser.Parse(["--continue"]);
        Assert.True(result.ContinueSession);
    }

    #endregion

    #region Parse — Help

    [Fact]
    public void Parse_Help_ShortFlag()
    {
        var result = BridgeMainArgsParser.Parse(["-h"]);
        Assert.True(result.Help);
    }

    [Fact]
    public void Parse_Help_LongFlag()
    {
        var result = BridgeMainArgsParser.Parse(["--help"]);
        Assert.True(result.Help);
    }

    #endregion

    #region Parse — 交叉验证

    [Fact]
    public void Parse_CapacityWithSpawnSession_ReturnsError()
    {
        var result = BridgeMainArgsParser.Parse(["--capacity", "3", "--spawn", "session"]);
        Assert.True(result.HasError);
        Assert.Contains("capacity", result.Error!);
    }

    [Fact]
    public void Parse_CapacityWithSpawnSameDir_Ok()
    {
        var result = BridgeMainArgsParser.Parse(["--capacity", "3", "--spawn", "same-dir"]);
        Assert.False(result.HasError);
        Assert.Equal(3, result.Capacity);
        Assert.Equal(BridgeSpawnMode.SameDir, result.SpawnMode);
    }

    [Fact]
    public void Parse_SessionIdWithSpawn_ReturnsError()
    {
        var result = BridgeMainArgsParser.Parse(["--session-id", "cse_123", "--spawn", "same-dir"]);
        Assert.True(result.HasError);
        Assert.Contains("session-id", result.Error!);
    }

    [Fact]
    public void Parse_ContinueWithCapacity_ReturnsError()
    {
        var result = BridgeMainArgsParser.Parse(["--continue", "--capacity", "3"]);
        Assert.True(result.HasError);
        Assert.Contains("continue", result.Error!);
    }

    [Fact]
    public void Parse_SessionIdWithContinue_ReturnsError()
    {
        var result = BridgeMainArgsParser.Parse(["--session-id", "cse_123", "--continue"]);
        Assert.True(result.HasError);
        Assert.Contains("mutually exclusive", result.Error!);
    }

    #endregion

    #region Parse — 组合参数

    [Fact]
    public void Parse_MultipleFlags()
    {
        var result = BridgeMainArgsParser.Parse(["-v", "--sandbox", "--name", "test", "--spawn", "worktree"]);
        Assert.True(result.Verbose);
        Assert.True(result.Sandbox);
        Assert.Equal("test", result.Name);
        Assert.Equal(BridgeSpawnMode.Worktree, result.SpawnMode);
    }

    [Fact]
    public void Parse_UnknownArgs_ReportsError()
    {
        var result = BridgeMainArgsParser.Parse(["--unknown-arg", "value"]);
        Assert.True(result.HasError);
        Assert.Contains("Unknown option", result.Error);
    }

    #endregion

    #region GetHelpText

    [Fact]
    public void GetHelpText_ContainsUsage()
    {
        var help = BridgeMainArgsParser.GetHelpText();
        Assert.Contains("Usage:", help);
        Assert.Contains("remote-control", help);
        Assert.Contains("--verbose", help);
        Assert.Contains("--spawn", help);
        Assert.Contains("--capacity", help);
    }

    #endregion
}
