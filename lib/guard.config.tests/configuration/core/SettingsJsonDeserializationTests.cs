
namespace Guard.Tests.Configuration;

/// <summary>
/// SettingsJson 反序列化 BDD 测试
/// 新结构: 顶层只有 vendor + current 两个分支
/// </summary>
public class SettingsJsonDeserializationTests
{
    private static readonly IModelConfigLoader Loader = new ModelConfigLoader();
    private static readonly string DefaultAnthropicModelId = Loader.GetDefaultModelId("anthropic");
    private static readonly string DefaultOpenAiModelId = Loader.GetDefaultModelId("openai");

    #region 场景1: 完整 settings.json 反序列化

    [Fact]
    public void Given_完整SettingsJson_When_反序列化_Then_所有字段正确映射()
    {
        var json = $$"""
            {
              "vendor": {
                "anthropic": {
                  "provider": "anthropic",
                  "model": "{{DefaultAnthropicModelId}}",
                  "endpoint": null
                }
              },
              "current": {
                "profile": "anthropic",
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
            }
            """;

        var settings = JsonSerializer.Deserialize(json, ConfigJsonContext.Default.SettingsJson);

        settings.Should().NotBeNull();
        // vendor
        settings!.Vendor.Should().NotBeNull();
        settings.Vendor!["anthropic"].Provider.Should().Be("anthropic");
        settings.Vendor["anthropic"].Model.Should().Be(DefaultAnthropicModelId);

        // current
        settings.Current.Should().NotBeNull();
        settings.Current!.Profile.Should().Be("anthropic");
        settings.Current.EffortLevel.Should().Be("high");
        settings.Current.DefaultShell.Should().Be("powershell");
        settings.Current.FastMode.Should().BeTrue();
        settings.Current.Language.Should().Be("zh-CN");
        settings.Current.AutoMemoryEnabled.Should().BeTrue();
        settings.Current.AutoDreamEnabled.Should().BeFalse();
        settings.Current.ShowThinkingSummaries.Should().BeTrue();

        settings.Current.Env.Should().NotBeNull();
        settings.Current.Env!["AGNES_API_KEY"].Should().Be("sk-agnes-api-key-value");

        settings.Current.Permissions.Should().NotBeNull();
        settings.Current.Permissions!.Allow.Should().Contain("Bash(npm test)");
        settings.Current.Permissions.Deny.Should().Contain("Bash(rm -rf)");
        settings.Current.Permissions.DefaultMode.Should().Be("autoAccept");

        settings.Current.Hooks.Should().NotBeNull();
        settings.Current.Hooks!.Should().ContainKey("PreToolUse");
        settings.Current.Hooks!["PreToolUse"][0].Command.Should().Be("echo before");

        settings.Current.McpServers.Should().NotBeNull();
        settings.Current.McpServers!["my-server"].Command.Should().Be("node");

        settings.Current.Sandbox.Should().NotBeNull();
        settings.Current.Sandbox!.Enabled.Should().BeTrue();
        settings.Current.Sandbox.Mode.Should().Be("docker");

        settings.Current.EnabledPlugins.Should().NotBeNull();
        settings.Current.EnabledPlugins!["dream"].Enabled.Should().BeTrue();

        settings.Current.Worktree.Should().NotBeNull();
        settings.Current.Worktree!.SymlinkDirectories.Should().Contain("node_modules");
        settings.Current.Worktree.SparsePaths.Should().Contain("src/");
    }

    #endregion

    #region 场景2: 最小 settings.json

    [Fact]
    public void Given_仅包含vendor和profile的SettingsJson_When_反序列化_Then_其余字段为null()
    {
        var json = $$"""
            {
              "vendor": {
                "openai": { "provider": "openai", "model": "{{DefaultOpenAiModelId}}" }
              },
              "current": { "profile": "openai" }
            }
            """;

        var settings = JsonSerializer.Deserialize(json, ConfigJsonContext.Default.SettingsJson);

        settings.Should().NotBeNull();
        settings!.Current!.Profile.Should().Be("openai");
        settings.Current.EffortLevel.Should().BeNull();
        settings.Current.DefaultShell.Should().BeNull();
        settings.Current.FastMode.Should().BeNull();
        settings.Current.Env.Should().BeNull();
        settings.Current.Permissions.Should().BeNull();
        settings.Current.Hooks.Should().BeNull();
        settings.Current.McpServers.Should().BeNull();
        settings.Current.Sandbox.Should().BeNull();
    }

    #endregion

    #region 场景3: 空 settings.json

    [Fact]
    public void Given_空对象SettingsJson_When_反序列化_Then_vendor和current为null()
    {
        var json = "{}";

        var settings = JsonSerializer.Deserialize(json, ConfigJsonContext.Default.SettingsJson);

        settings.Should().NotBeNull();
        settings!.Vendor.Should().BeNull();
        settings.Current.Should().BeNull();
    }

    #endregion

    #region 场景4: 损坏 JSON 恢复

    [Fact]
    public void Given_损坏的JSON_When_反序列化_Then_抛出JsonException()
    {
        var json = "{ invalid json }";

        var act = () => JsonSerializer.Deserialize(json, ConfigJsonContext.Default.SettingsJson);
        act.Should().Throw<JsonException>();
    }

    #endregion

    #region 场景5: 序列化 + 反序列化往返

    [Fact]
    public void Given_SettingsJson对象_When_序列化再反序列化_Then_数据一致()
    {
        var original = new SettingsJson
        {
            Vendor = new Dictionary<string, ProfileSettings>(StringComparer.OrdinalIgnoreCase)
            {
                ["openai"] = new ProfileSettings
                {
                    Provider = "openai",
                    Model = DefaultOpenAiModelId,
                },
            },
            Current = new CurrentSettings
            {
                Profile = "openai",
                FastMode = true,
                Env = new Dictionary<string, string> { ["KEY"] = "value" },
                Permissions = new PermissionsSettings
                {
                    Allow = ["Bash(npm test)"],
                    DefaultMode = PermissionMode.Auto.ToValue(),
                },
            },
        };

        var json = JsonSerializer.Serialize(original, ConfigIndentedJsonContext.Default.SettingsJson);
        var deserialized = JsonSerializer.Deserialize(json, ConfigJsonContext.Default.SettingsJson);

        deserialized.Should().NotBeNull();
        deserialized!.Vendor!["openai"].Model.Should().Be(DefaultOpenAiModelId);
        deserialized.Current!.Profile.Should().Be("openai");
        deserialized.Current.FastMode.Should().BeTrue();
        deserialized.Current.Env!["KEY"].Should().Be("value");
        deserialized.Current.Permissions!.Allow.Should().ContainSingle("Bash(npm test)");
        deserialized.Current.Permissions.DefaultMode.Should().Be(PermissionMode.Auto.ToValue());
    }

    #endregion

    #region 场景6: SandboxSettings 新字段反序列化

    [Fact]
    public void Given_SandboxSettings含新字段_When_反序列化_Then_AllowedPathsRestrictNetworkMemoryLimitMb正确映射()
    {
        var json = """
            {
              "current": {
                "sandbox": {
                  "enabled": true,
                  "mode": "process",
                  "allowedPaths": ["/tmp", "/home"],
                  "restrictNetwork": true,
                  "memoryLimitMb": 512
                }
              }
            }
            """;

        var settings = JsonSerializer.Deserialize(json, ConfigJsonContext.Default.SettingsJson);
        settings.Should().NotBeNull();
        settings!.Current!.Sandbox.Should().NotBeNull();
        settings.Current.Sandbox!.Enabled.Should().BeTrue();
        settings.Current.Sandbox.Mode.Should().Be("process");
        settings.Current.Sandbox.AllowedPaths.Should().Equal("/tmp", "/home");
        settings.Current.Sandbox.RestrictNetwork.Should().BeTrue();
        settings.Current.Sandbox.MemoryLimitMb.Should().Be(512);
    }

    [Fact]
    public void Given_SandboxSettings仅旧字段_When_反序列化_Then_新字段为null()
    {
        var json = """
            {
              "current": {
                "sandbox": {
                  "enabled": false,
                  "mode": "soft"
                }
              }
            }
            """;

        var settings = JsonSerializer.Deserialize(json, ConfigJsonContext.Default.SettingsJson);
        settings.Should().NotBeNull();
        settings!.Current!.Sandbox.Should().NotBeNull();
        settings.Current.Sandbox!.Enabled.Should().BeFalse();
        settings.Current.Sandbox.Mode.Should().Be("soft");
        settings.Current.Sandbox.AllowedPaths.Should().BeNull();
        settings.Current.Sandbox.RestrictNetwork.Should().BeNull();
        settings.Current.Sandbox.MemoryLimitMb.Should().BeNull();
    }

    #endregion

    #region 场景7: GetActiveProfile 辅助方法

    [Fact]
    public void Given_CurrentProfile指向Vendor键_When_GetActiveProfile_Then_返回对应预设()
    {
        var settings = new SettingsJson
        {
            Vendor = new Dictionary<string, ProfileSettings>(StringComparer.OrdinalIgnoreCase)
            {
                ["sensenova"] = new ProfileSettings { Provider = "sensenova", Model = "sensenova-6.7-flash-lite" },
            },
            Current = new CurrentSettings { Profile = "sensenova" },
        };

        var profile = settings.GetActiveProfile();
        profile.Should().NotBeNull();
        profile!.Provider.Should().Be("sensenova");
        profile.Model.Should().Be("sensenova-6.7-flash-lite");
    }

    [Fact]
    public void Given_CurrentProfile不存在于Vendor_When_GetActiveProfile_Then_返回null()
    {
        var settings = new SettingsJson
        {
            Vendor = new Dictionary<string, ProfileSettings>(StringComparer.OrdinalIgnoreCase)
            {
                ["openai"] = new ProfileSettings { Provider = "openai" },
            },
            Current = new CurrentSettings { Profile = "nonexistent" },
        };

        settings.GetActiveProfile().Should().BeNull();
    }

    #endregion
}
