namespace MockServer.Core.Tests;

public sealed class MockServerConfigTests : IDisposable
{
    private readonly string _tempDir;

    public MockServerConfigTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"mockserver_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    #region LoadFromFile Tests

    [Fact]
    public void LoadFromFile_NullPath_ThrowsArgumentException()
    {
        var act = () => MockServerConfig.LoadFromFile(null!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void LoadFromFile_EmptyPath_ThrowsArgumentException()
    {
        var act = () => MockServerConfig.LoadFromFile("");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void LoadFromFile_NonexistentFile_ThrowsFileNotFoundException()
    {
        var path = Path.Combine(_tempDir, "nonexistent.json");
        var act = () => MockServerConfig.LoadFromFile(path);
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void LoadFromFile_ValidJson_LoadsConfigCorrectly()
    {
        var json = """
            {
              "port": 9999,
              "default_response": "custom default",
              "scripted_turns": [
                {
                  "text_response": "hello",
                  "thinking_content": "thinking..."
                },
                {
                  "tool_calls": [
                    {
                      "tool_name": "read",
                      "arguments": "{\"file_path\":\"test.txt\"}",
                      "tool_call_id": "call_123"
                    }
                  ],
                  "follow_up_text": "done reading"
                }
              ]
            }
            """;
        var path = Path.Combine(_tempDir, "config.json");
        File.WriteAllText(path, json);

        var config = MockServerConfig.LoadFromFile(path);

        config.Port.Should().Be(9999);
        config.DefaultResponse.Should().Be("custom default");
        config.ScriptedTurns.Should().HaveCount(2);
        config.ScriptedTurns[0].TextResponse.Should().Be("hello");
        config.ScriptedTurns[0].ThinkingContent.Should().Be("thinking...");
        config.ScriptedTurns[1].ToolCalls.Should().HaveCount(1);
        config.ScriptedTurns[1].ToolCalls![0].ToolName.Should().Be("read");
        config.ScriptedTurns[1].ToolCalls![0].Arguments.Should().Be("{\"file_path\":\"test.txt\"}");
        config.ScriptedTurns[1].ToolCalls![0].ToolCallId.Should().Be("call_123");
        config.ScriptedTurns[1].FollowUpText.Should().Be("done reading");
    }

    [Fact]
    public void LoadFromFile_InvalidJson_ThrowsJsonException()
    {
        var path = Path.Combine(_tempDir, "bad.json");
        File.WriteAllText(path, "not valid json{{{");

        var act = () => MockServerConfig.LoadFromFile(path);
        act.Should().Throw<System.Text.Json.JsonException>();
    }

    [Fact]
    public void LoadFromFile_DeserializesToNull_ThrowsInvalidOperationException()
    {
        var path = Path.Combine(_tempDir, "null.json");
        File.WriteAllText(path, "null");

        var act = () => MockServerConfig.LoadFromFile(path);
        act.Should().Throw<InvalidOperationException>();
    }

    #endregion

    #region LoadFromFileOrDefault Tests

    [Fact]
    public void LoadFromFileOrDefault_NonexistentFile_ReturnsDefaultConfig()
    {
        var path = Path.Combine(_tempDir, "missing.json");

        var config = MockServerConfig.LoadFromFileOrDefault(path);

        config.Should().NotBeNull();
        config.Port.Should().Be(0);
        config.DefaultResponse.Should().Be("Mock response (script exhausted).");
        config.ScriptedTurns.Should().BeEmpty();
    }

    [Fact]
    public void LoadFromFileOrDefault_ExistingFile_LoadsConfig()
    {
        var json = """{ "port": 8080 }""";
        var path = Path.Combine(_tempDir, "cfg.json");
        File.WriteAllText(path, json);

        var config = MockServerConfig.LoadFromFileOrDefault(path);

        config.Port.Should().Be(8080);
    }

    [Fact]
    public void LoadFromFileOrDefault_InvalidJson_ReturnsDefaultConfig()
    {
        var path = Path.Combine(_tempDir, "broken.json");
        File.WriteAllText(path, "{bad json");

        var config = MockServerConfig.LoadFromFileOrDefault(path);

        config.Should().NotBeNull();
        config.Port.Should().Be(0);
        config.ScriptedTurns.Should().BeEmpty();
    }

    [Fact]
    public void LoadFromFileOrDefault_RelativePathFallbackToBaseDirectory_WhenFileNotInCwd()
    {
        var fileName = $"mockserver_fallback_{Guid.NewGuid():N}.json";
        var baseDirFile = Path.Combine(AppContext.BaseDirectory, fileName);
        try
        {
            File.WriteAllText(baseDirFile, """{ "port": 7777, "default_response": "from basedir" }""");

            var config = MockServerConfig.LoadFromFileOrDefault(fileName);

            config.Port.Should().Be(7777);
            config.DefaultResponse.Should().Be("from basedir");
        }
        finally
        {
            if (File.Exists(baseDirFile))
                File.Delete(baseDirFile);
        }
    }

    [Fact]
    public void LoadFromFileOrDefault_SpecificPathTakesPrecedenceOverBaseDirectory()
    {
        var fileName = $"mockserver_precedence_{Guid.NewGuid():N}.json";
        var specificFile = Path.Combine(_tempDir, fileName);
        var baseDirFile = Path.Combine(AppContext.BaseDirectory, fileName);
        try
        {
            File.WriteAllText(specificFile, """{ "port": 1111, "default_response": "from specific" }""");
            File.WriteAllText(baseDirFile, """{ "port": 2222, "default_response": "from basedir" }""");

            var config = MockServerConfig.LoadFromFileOrDefault(specificFile);

            config.Port.Should().Be(1111);
            config.DefaultResponse.Should().Be("from specific");
        }
        finally
        {
            if (File.Exists(baseDirFile))
                File.Delete(baseDirFile);
        }
    }

    [Fact]
    public void LoadFromFileOrDefault_FileNotInCwdOrBaseDir_ReturnsDefault()
    {
        var config = MockServerConfig.LoadFromFileOrDefault($"totally_nonexistent_mockserver_{Guid.NewGuid():N}.json");

        config.Should().NotBeNull();
        config.Port.Should().Be(0);
        config.ScriptedTurns.Should().BeEmpty();
        config.DefaultResponse.Should().Be("Mock response (script exhausted).");
    }

    #endregion

    #region Default Values Tests

    [Fact]
    public void DefaultConfig_HasExpectedDefaults()
    {
        var config = new MockServerConfig();

        config.Port.Should().Be(0);
        config.ScriptedTurns.Should().BeEmpty();
        config.DefaultResponse.Should().Be("Mock response (script exhausted).");
    }

    [Fact]
    public void ScriptedTurn_Defaults_AreCorrect()
    {
        var turn = new ScriptedTurn();

        turn.TextResponse.Should().BeNull();
        turn.ToolCalls.Should().BeNull();
        turn.ThinkingContent.Should().BeNull();
        turn.FollowUpText.Should().BeNull();
    }

    [Fact]
    public void ToolCallConfig_Defaults_AreCorrect()
    {
        var tc = new ToolCallConfig();

        tc.ToolName.Should().Be("");
        tc.Arguments.Should().Be("{}");
        tc.ToolCallId.Should().BeNull();
    }

    #endregion
}
