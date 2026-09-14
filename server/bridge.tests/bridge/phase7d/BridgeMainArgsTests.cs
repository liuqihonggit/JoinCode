
namespace Bridge.Tests.Phase7D;

public sealed class BridgeMainArgsTests
{
    #region Parse — 基本参数

    [Fact]
    public void Parse_EmptyArgs_ReturnsDefaults()
    {
        var result = BridgeMainArgsParser.Parse([]);

        Assert.False(result.DebugLog);
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
    public void Parse_DebugLog_ShortFlag()
    {
        var result = BridgeMainArgsParser.Parse([JccCliArgEnumConstants.DebugLogAlias__d]);
        Assert.True(result.DebugLog);
    }

    [Fact]
    public void Parse_DebugLog_LongFlag()
    {
        var result = BridgeMainArgsParser.Parse([JccCliArgEnumConstants.DebugLog]);
        Assert.True(result.DebugLog);
    }

    [Fact]
    public void Parse_Sandbox_Enabled()
    {
        var result = BridgeMainArgsParser.Parse([JccCliArgEnumConstants.Sandbox]);
        Assert.True(result.Sandbox);
    }

    [Fact]
    public void Parse_Sandbox_Disabled()
    {
        var result = BridgeMainArgsParser.Parse([JccCliArgEnumConstants.NoSandbox]);
        Assert.False(result.Sandbox);
    }

    [Fact]
    public void Parse_DebugFile()
    {
        var result = BridgeMainArgsParser.Parse([JccCliArgEnumConstants.DebugFile, "/tmp/bridge.log"]);
        Assert.Equal("/tmp/bridge.log", result.DebugFile);
    }

    [Fact]
    public void Parse_DebugFile_MissingValue_ReturnsError()
    {
        var result = BridgeMainArgsParser.Parse([JccCliArgEnumConstants.DebugFile]);
        Assert.True(result.HasError);
        Assert.Contains("debug-file", result.Error!);
    }

    [Fact]
    public void Parse_SessionTimeout()
    {
        var result = BridgeMainArgsParser.Parse([JccCliArgEnumConstants.SessionTimeout, "3600"]);
        Assert.Equal(3600000, result.SessionTimeoutMs);
    }

    [Fact]
    public void Parse_SessionTimeout_InvalidValue_ReturnsError()
    {
        var result = BridgeMainArgsParser.Parse([JccCliArgEnumConstants.SessionTimeout, "abc"]);
        Assert.True(result.HasError);
    }

    [Fact]
    public void Parse_SessionTimeout_Zero_ReturnsError()
    {
        var result = BridgeMainArgsParser.Parse([JccCliArgEnumConstants.SessionTimeout, "0"]);
        Assert.True(result.HasError);
    }

    [Fact]
    public void Parse_SessionTimeout_MissingValue_ReturnsError()
    {
        var result = BridgeMainArgsParser.Parse([JccCliArgEnumConstants.SessionTimeout]);
        Assert.True(result.HasError);
    }

    [Fact]
    public void Parse_PermissionMode()
    {
        var result = BridgeMainArgsParser.Parse([JccCliArgEnumConstants.PermissionMode, "auto-accept"]);
        Assert.Equal("auto-accept", result.PermissionMode);
    }

    [Fact]
    public void Parse_PermissionMode_MissingValue_ReturnsError()
    {
        var result = BridgeMainArgsParser.Parse([JccCliArgEnumConstants.PermissionMode]);
        Assert.True(result.HasError);
    }

    [Fact]
    public void Parse_Name()
    {
        var result = BridgeMainArgsParser.Parse([JccCliArgEnumConstants.Name, "my-bridge"]);
        Assert.Equal("my-bridge", result.Name);
    }

    [Fact]
    public void Parse_Name_MissingValue_ReturnsError()
    {
        var result = BridgeMainArgsParser.Parse([JccCliArgEnumConstants.Name]);
        Assert.True(result.HasError);
    }

    #endregion

    #region Parse — SpawnMode

    [Fact]
    public void Parse_SpawnSession()
    {
        var result = BridgeMainArgsParser.Parse([JccCliArgEnumConstants.Spawn, "session"]);
        Assert.Equal(BridgeSpawnMode.SingleSession, result.SpawnMode);
    }

    [Fact]
    public void Parse_SpawnSameDir()
    {
        var result = BridgeMainArgsParser.Parse([JccCliArgEnumConstants.Spawn, "same-dir"]);
        Assert.Equal(BridgeSpawnMode.SameDir, result.SpawnMode);
    }

    [Fact]
    public void Parse_SpawnWorktree()
    {
        var result = BridgeMainArgsParser.Parse([JccCliArgEnumConstants.Spawn, "worktree"]);
        Assert.Equal(BridgeSpawnMode.Worktree, result.SpawnMode);
    }

    [Fact]
    public void Parse_SpawnInvalid_ReturnsError()
    {
        var result = BridgeMainArgsParser.Parse([JccCliArgEnumConstants.Spawn, "invalid"]);
        Assert.True(result.HasError);
        Assert.Contains("spawn", result.Error!);
    }

    [Fact]
    public void Parse_SpawnMissingValue_ReturnsError()
    {
        var result = BridgeMainArgsParser.Parse([JccCliArgEnumConstants.Spawn]);
        Assert.True(result.HasError);
    }

    #endregion

    #region Parse — Capacity / CreateSessionInDir

    [Fact]
    public void Parse_Capacity()
    {
        var result = BridgeMainArgsParser.Parse([JccCliArgEnumConstants.Capacity, "5"]);
        Assert.Equal(5, result.Capacity);
    }

    [Fact]
    public void Parse_Capacity_InvalidValue_ReturnsError()
    {
        var result = BridgeMainArgsParser.Parse([JccCliArgEnumConstants.Capacity, "abc"]);
        Assert.True(result.HasError);
    }

    [Fact]
    public void Parse_Capacity_Zero_ReturnsError()
    {
        var result = BridgeMainArgsParser.Parse([JccCliArgEnumConstants.Capacity, "0"]);
        Assert.True(result.HasError);
    }

    [Fact]
    public void Parse_CreateSessionInDir()
    {
        var result = BridgeMainArgsParser.Parse([JccCliArgEnumConstants.CreateSessionInDir]);
        Assert.True(result.CreateSessionInDir);
    }

    [Fact]
    public void Parse_NoCreateSessionInDir()
    {
        var result = BridgeMainArgsParser.Parse([JccCliArgEnumConstants.NoCreateSessionInDir]);
        Assert.False(result.CreateSessionInDir);
    }

    #endregion

    #region Parse — SessionId / Continue

    [Fact]
    public void Parse_SessionId()
    {
        var result = BridgeMainArgsParser.Parse([JccCliArgEnumConstants.SessionId, "cse_123"]);
        Assert.Equal("cse_123", result.SessionId);
    }

    [Fact]
    public void Parse_SessionId_MissingValue_ReturnsError()
    {
        var result = BridgeMainArgsParser.Parse([JccCliArgEnumConstants.SessionId]);
        Assert.True(result.HasError);
    }

    [Fact]
    public void Parse_Continue_ShortFlag()
    {
        var result = BridgeMainArgsParser.Parse([JccCliArgEnumConstants.ContinueAlias__c]);
        Assert.True(result.ContinueSession);
    }

    [Fact]
    public void Parse_Continue_LongFlag()
    {
        var result = BridgeMainArgsParser.Parse([JccCliArgEnumConstants.Continue]);
        Assert.True(result.ContinueSession);
    }

    #endregion

    #region Parse — Help

    [Fact]
    public void Parse_Help_ShortFlag()
    {
        var result = BridgeMainArgsParser.Parse([JccCliArgEnumConstants.HelpAlias__h]);
        Assert.True(result.Help);
    }

    [Fact]
    public void Parse_Help_LongFlag()
    {
        var result = BridgeMainArgsParser.Parse([JccCliArgEnumConstants.Help]);
        Assert.True(result.Help);
    }

    #endregion

    #region Parse — 交叉验证

    [Fact]
    public void Parse_CapacityWithSpawnSession_ReturnsError()
    {
        var result = BridgeMainArgsParser.Parse([JccCliArgEnumConstants.Capacity, "3", JccCliArgEnumConstants.Spawn, "session"]);
        Assert.True(result.HasError);
        Assert.Contains("capacity", result.Error!);
    }

    [Fact]
    public void Parse_CapacityWithSpawnSameDir_Ok()
    {
        var result = BridgeMainArgsParser.Parse([JccCliArgEnumConstants.Capacity, "3", JccCliArgEnumConstants.Spawn, "same-dir"]);
        Assert.False(result.HasError);
        Assert.Equal(3, result.Capacity);
        Assert.Equal(BridgeSpawnMode.SameDir, result.SpawnMode);
    }

    [Fact]
    public void Parse_SessionIdWithSpawn_ReturnsError()
    {
        var result = BridgeMainArgsParser.Parse([JccCliArgEnumConstants.SessionId, "cse_123", JccCliArgEnumConstants.Spawn, "same-dir"]);
        Assert.True(result.HasError);
        Assert.Contains("session-id", result.Error!);
    }

    [Fact]
    public void Parse_ContinueWithCapacity_ReturnsError()
    {
        var result = BridgeMainArgsParser.Parse([JccCliArgEnumConstants.Continue, JccCliArgEnumConstants.Capacity, "3"]);
        Assert.True(result.HasError);
        Assert.Contains("continue", result.Error!);
    }

    [Fact]
    public void Parse_SessionIdWithContinue_ReturnsError()
    {
        var result = BridgeMainArgsParser.Parse([JccCliArgEnumConstants.SessionId, "cse_123", JccCliArgEnumConstants.Continue]);
        Assert.True(result.HasError);
        Assert.Contains("mutually exclusive", result.Error!);
    }

    #endregion

    #region Parse — 组合参数

    [Fact]
    public void Parse_MultipleFlags()
    {
        var result = BridgeMainArgsParser.Parse([JccCliArgEnumConstants.DebugLogAlias__d, JccCliArgEnumConstants.Sandbox, JccCliArgEnumConstants.Name, "test", JccCliArgEnumConstants.Spawn, "worktree"]);
        Assert.True(result.DebugLog);
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
        Assert.Contains(JccCliArg.DebugLog.ToValue(), help);
        Assert.Contains(JccCliArg.Spawn.ToValue(), help);
        Assert.Contains(JccCliArg.Capacity.ToValue(), help);
    }

    #endregion
}
