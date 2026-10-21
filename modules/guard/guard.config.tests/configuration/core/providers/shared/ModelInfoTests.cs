namespace Core.Configuration.Tests.Providers;

/// <summary>
/// ModelInfo 信息类测试（TASK061 — 1M 上下文时代模型元数据）—
/// 两源投影（ModelEntry / ModelItemConfig）+ 强校验（contextWindow<=0 抛 ConfigurationException
/// 带模型名/供应商/修复指引，对齐"模型配置里写模型名称+上下文大小，不对的直接报错"）。
/// </summary>
public sealed class ModelInfoTests
{
    /// <summary>FromEntry 投影 — 四字段全透传。</summary>
    [Fact]
    public void FromEntry_ProjectsAllFields()
    {
        var info = ModelInfo.FromEntry("zhipu", new ModelEntry("glm-5.2", "GLM 5.2", 1_048_576, "旗舰"));

        info.ModelId.Should().Be("glm-5.2");
        info.Provider.Should().Be("zhipu");
        info.DisplayName.Should().Be("GLM 5.2");
        info.ContextWindow.Should().Be(1_048_576);
    }

    /// <summary>FromModelItemConfig 投影 — DisplayName 空时退化 ModelId（配置容错）。</summary>
    [Fact]
    public void FromModelItemConfig_EmptyDisplayName_FallsBackToId()
    {
        var config = new ModelItemConfig { Id = "agnes-2.5-pro-alpha", DisplayName = "", ContextWindow = 128_000 };

        var info = ModelInfo.FromModelItemConfig("agnes", config);

        info.DisplayName.Should().Be("agnes-2.5-pro-alpha");
        info.ContextWindow.Should().Be(128_000);
    }

    /// <summary>零窗口（agnes 脏数据实锤形态）— 抛 ConfigurationException 带模型/供应商/修复指引。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ValidateOrThrow_NonPositiveWindow_ThrowsWithGuidance(int badWindow)
    {
        var info = ModelInfo.FromModelItemConfig("agnes", new ModelItemConfig { Id = "agnes-2.5-pro-alpha", ContextWindow = badWindow });

        var act = () => info.ValidateOrThrow();

        var ex = act.Should().Throw<JoinCode.Abstractions.Exceptions.ConfigurationException>().Which;
        ex.Message.Should().Contain("agnes/agnes-2.5-pro-alpha");
        ex.Message.Should().Contain("providers_llm");
        ex.Message.Should().Contain("JCC_MAX_CONTEXT_TOKENS");
        ex.ConfigurationKey.Should().Be("agnes/agnes-2.5-pro-alpha/contextWindow");
    }

    /// <summary>合法窗口 — 链式返回自身（1M 不被兜底截断）。</summary>
    [Fact]
    public void ValidateOrThrow_ValidWindow_ReturnsSelf()
    {
        var info = ModelInfo.FromModelItemConfig("sensenova", new ModelItemConfig { Id = "glm-5.2", ContextWindow = 1_048_576 });

        info.ValidateOrThrow().Should().BeSameAs(info);
    }
}
