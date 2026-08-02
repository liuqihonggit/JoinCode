
namespace Core.Tests.Hooks.Configuration;

/// <summary>
/// HookSettingsFile JSON 序列化 E2E 测试 — 验证 AOT 模式下不会递归卡死
/// </summary>
public class HookSettingsFileSerializationTests
{
    [Fact(Timeout = 5000)]
    public async Task Serialize_HookSettingsFile_ShouldNotThrow()
    {
        // Arrange
        var settings = new HookSettingsFile
        {
            Hooks = new Dictionary<string, List<HookMatcher>>
            {
                ["session_start"] = new List<HookMatcher>
                {
                    HookMatcher.Create(null, new BashCommandHook { Command = "echo hello" })
                }
            }
        };

        // Act
        var json = JsonSerializer.Serialize(settings, HooksJsonContext.Default.HookSettingsFile);

        // Assert
        json.Should().NotBeNullOrEmpty();
        json.Should().Contain("echo hello");
        await Task.CompletedTask.ConfigureAwait(true);
    }

    [Fact(Timeout = 5000)]
    public async Task Deserialize_HookSettingsFile_ShouldRestoreData()
    {
        // Arrange
        var settings = new HookSettingsFile
        {
            Hooks = new Dictionary<string, List<HookMatcher>>
            {
                ["tool_end"] = new List<HookMatcher>
                {
                    HookMatcher.Create("Bash", new PromptHook { Prompt = "Check result" })
                }
            }
        };

        // Act
        var json = JsonSerializer.Serialize(settings, HooksJsonContext.Default.HookSettingsFile);
        var deserialized = JsonSerializer.Deserialize(json, HooksJsonContext.Default.HookSettingsFile);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Hooks.ContainsKey("tool_end").Should().BeTrue();
        deserialized.Hooks["tool_end"].Should().HaveCount(1);
        await Task.CompletedTask.ConfigureAwait(true);
    }
}
