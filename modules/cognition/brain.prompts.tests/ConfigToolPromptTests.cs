namespace Core.Tests.Prompts;

/// <summary>
/// ConfigTool 工具描述测试 —— 锁定「模型能看见可配置键清单」这条契约。
/// <para>旧实现里 <c>ConfigToolPrompt.GeneratePrompt(supportedSettings, modelOptions)</c> 零调用方，
/// 于是那份设置清单从来没到达过模型：模型只知道「有个 config 工具」，不知道能设哪些键、
/// 每个键接受什么值，只能猜（猜 <c>permissions.mode</c>、猜 <c>model: opus</c>）而被逐个拒绝。</para>
/// <para>现在 <see cref="ConfigToolPrompt.GetDescription"/> 无参，内容由
/// <see cref="ConfigKeyRegistry"/> 直接生成，并由源码生成器注册进 <see cref="ToolPromptRegistration"/> ——
/// 这里断言注册表真的解析到了「长描述」而不是那句一行短描述。</para>
/// </summary>
public sealed class ConfigToolPromptTests {
    private static string ConfigToolName => ConfigToolNameEnumConstants.Config;

    /// <summary>取注册表里的详细描述；没注册就直接失败 —— 那正是这条测试要防的回归。</summary>
    private static string DetailedDescription() =>
        ToolPromptRegistration.GetDetailedDescription(ConfigToolName)
        ?? throw new InvalidOperationException("config 工具的详细描述未注册（ToolPromptRegistration 里没有 config）");

    [Fact]
    public void DetailedDescription_IsRegisteredAndIsNotTheOneLineSummary() {
        ToolPromptRegistration.GetDetailedDescription(ConfigToolName)
            .Should().NotBeNull("config 工具必须有注册的详细描述，否则模型只能看到 [McpTool] 上的一句短描述");

        var detailed = DetailedDescription();
        detailed.Should().NotBe(ConfigToolPrompt.Description);
        detailed.Length.Should().BeGreaterThan(ConfigToolPrompt.Description.Length * 10);
    }

    [Fact]
    public void DetailedDescription_ListsEveryRegisteredKey() {
        var detailed = DetailedDescription();

        foreach (var (key, _) in ConfigKeyRegistry.All)
            detailed.Should().Contain($"- {key}", "清单里每个键都要出现在工具描述里，否则模型会以为它不存在");
    }

    [Fact]
    public void DetailedDescription_ShowsOptionsForEnumeratedKeys() {
        var detailed = DetailedDescription();

        detailed.Should().Contain("permissions.defaultMode: \"plan\"", "枚举键要带可选值，模型才能一次写对");
        detailed.Should().Contain("theme: \"dark\"");
        detailed.Should().Contain("debuglog: true/false", "布尔键要标出 true/false");
    }

    [Fact]
    public void DetailedDescription_SplitsGlobalAndProjectSettings() {
        var detailed = DetailedDescription();

        detailed.Should().Contain("global.json");
        detailed.Should().Contain("settings.json");

        // 分组必须与该键真实的存储源一致 —— 说错了模型就会去错地方找
        var globalSection = detailed[..detailed.IndexOf("### 项目设置", StringComparison.Ordinal)];
        globalSection.Should().Contain("- theme");
        globalSection.Should().NotContain("- model", "model 存在 settings.json 的 vendor.{profile}.model 里");
    }

    [Fact]
    public void DetailedDescription_PointsModelCandidatesToModelSearch() {
        var detailed = DetailedDescription();

        detailed.Should().Contain("model_search", "模型候选是运行期才知道的，静态清单不该编造 sonnet/opus 这类占位值");
        detailed.Should().NotContain("sonnet、opus、haiku、best", "旧的写死占位值会把任何真实模型 id 判成非法");
    }
}
