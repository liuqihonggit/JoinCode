
namespace Guard.Tests.Configuration;

/// <summary>
/// SettingsJson 反序列化 BDD 测试
/// 对齐 TS 版 SettingsSchema 的 JSON 结构
/// </summary>
public class SettingsJsonDeserializationTests
{
    #region 场景1: 完整 settings.json 反序列化

    [Fact]
    public void Given_完整SettingsJson_When_反序列化_Then_所有字段正确映射()
    {
        // Given: 完整的 settings.json 内容
        var json = """
            {
              "model": "claude-sonnet-4-20250514",
              "effortLevel": "high",
              "defaultShell": "powershell",
              "fastMode": true,
              "language": "zh-CN",
              "autoMemoryEnabled": true,
              "autoDreamEnabled": false,
              "showThinkingSummaries": true,
              "env": {
                "AGNES_API_KEY": "sk-agnes-api-key-value"
              },
              "permissions": {
                "allow": ["Bash(npm test)", "ReadFile"],
                "deny": ["Bash(rm -rf)"],
                "defaultMode": "autoAccept"
              },
              "hooks": {
                "PreToolUse": [
                  { "type": "command", "command": "echo before", "matcher": "Bash" }
                ]
              },
              "mcpServers": {
                "my-server": {
                  "type": "stdio",
                  "command": "node",
                  "args": ["server.js"]
                }
              },
              "sandbox": {
                "enabled": true,
                "mode": "docker"
              },
              "enabledPlugins": {
                "dream": { "enabled": true }
              },
              "worktree": {
                "symlinkDirectories": ["node_modules"],
                "sparsePaths": ["src/"]
              }
            }
            """;

        // When: 反序列化
        var settings = JsonSerializer.Deserialize(json, ConfigJsonContext.Default.SettingsJson);

        // Then: 所有字段正确映射
        settings.Should().NotBeNull();
        settings!.Model.Should().Be("claude-sonnet-4-20250514");
        settings.EffortLevel.Should().Be("high");
        settings.DefaultShell.Should().Be("powershell");
        settings.FastMode.Should().BeTrue();
        settings.Language.Should().Be("zh-CN");
        settings.AutoMemoryEnabled.Should().BeTrue();
        settings.AutoDreamEnabled.Should().BeFalse();
        settings.ShowThinkingSummaries.Should().BeTrue();

        // env
        settings.Env.Should().NotBeNull();
        settings.Env!["AGNES_API_KEY"].Should().Be("sk-agnes-api-key-value");

        // permissions
        settings.Permissions.Should().NotBeNull();
        settings.Permissions!.Allow.Should().Contain("Bash(npm test)");
        settings.Permissions.Deny.Should().Contain("Bash(rm -rf)");
        settings.Permissions.DefaultMode.Should().Be("autoAccept");

        // hooks
        settings.Hooks.Should().NotBeNull();
        settings.Hooks!.Should().ContainKey("PreToolUse");
        settings.Hooks!["PreToolUse"][0].Command.Should().Be("echo before");

        // mcpServers
        settings.McpServers.Should().NotBeNull();
        settings.McpServers!["my-server"].Command.Should().Be("node");

        // sandbox
        settings.Sandbox.Should().NotBeNull();
        settings.Sandbox!.Enabled.Should().BeTrue();
        settings.Sandbox.Mode.Should().Be("docker");

        // enabledPlugins
        settings.EnabledPlugins.Should().NotBeNull();
        settings.EnabledPlugins!["dream"].Enabled.Should().BeTrue();

        // worktree
        settings.Worktree.Should().NotBeNull();
        settings.Worktree!.SymlinkDirectories.Should().Contain("node_modules");
        settings.Worktree.SparsePaths.Should().Contain("src/");
    }

    #endregion

    #region 场景2: 最小 settings.json（仅部分字段）

    [Fact]
    public void Given_仅包含model字段的SettingsJson_When_反序列化_Then_其余字段为null()
    {
        // Given
        var json = """{ "model": "gpt-4o" }""";

        // When
        var settings = JsonSerializer.Deserialize(json, ConfigJsonContext.Default.SettingsJson);

        // Then
        settings.Should().NotBeNull();
        settings!.Model.Should().Be("gpt-4o");
        settings.EffortLevel.Should().BeNull();
        settings.DefaultShell.Should().BeNull();
        settings.FastMode.Should().BeNull();
        settings.Env.Should().BeNull();
        settings.Permissions.Should().BeNull();
        settings.Hooks.Should().BeNull();
        settings.McpServers.Should().BeNull();
        settings.Sandbox.Should().BeNull();
    }

    #endregion

    #region 场景3: 空 settings.json

    [Fact]
    public void Given_空对象SettingsJson_When_反序列化_Then_所有字段为null或默认值()
    {
        // Given
        var json = "{}";

        // When
        var settings = JsonSerializer.Deserialize(json, ConfigJsonContext.Default.SettingsJson);

        // Then
        settings.Should().NotBeNull();
        settings!.Model.Should().BeNull();
        settings.FastMode.Should().BeNull();
        settings.Env.Should().BeNull();
    }

    #endregion

    #region 场景4: 损坏 JSON 恢复

    [Fact]
    public void Given_损坏的JSON_When_反序列化_Then_抛出JsonException()
    {
        // Given
        var json = "{ invalid json }";

        // When/Then
        var act = () => JsonSerializer.Deserialize(json, ConfigJsonContext.Default.SettingsJson);
        act.Should().Throw<JsonException>();
    }

    #endregion

    #region 场景5: 序列化 + 反序列化往返

    [Fact]
    public void Given_SettingsJson对象_When_序列化再反序列化_Then_数据一致()
    {
        // Given
        var original = new SettingsJson
        {
            Model = "gpt-4o",
            FastMode = true,
            Env = new Dictionary<string, string> { ["KEY"] = "value" },
            Permissions = new PermissionsSettings
            {
                Allow = ["Bash(npm test)"],
                DefaultMode = "default",
            },
        };

        // When
        var json = JsonSerializer.Serialize(original, ConfigIndentedJsonContext.Default.SettingsJson);
        var deserialized = JsonSerializer.Deserialize(json, ConfigJsonContext.Default.SettingsJson);

        // Then
        deserialized.Should().NotBeNull();
        deserialized!.Model.Should().Be("gpt-4o");
        deserialized.FastMode.Should().BeTrue();
        deserialized.Env!["KEY"].Should().Be("value");
        deserialized.Permissions!.Allow.Should().ContainSingle("Bash(npm test)");
        deserialized.Permissions.DefaultMode.Should().Be("default");
    }

    #endregion
}
