namespace Core.Prompts.Tests;

/// <summary>
/// 输出风格 Section 测试 — settings.current.outputStyle 键消费方（对齐 TS outputStyle 子系统）：
/// 内建名映射既有两 section 内容；自定义名经 OutputStyleLoader 双层目录加载；未配置/找不到 null。
/// PromptConfigSnapshot.SetCurrent 注入（EnvironmentSection 先例）。
/// </summary>
public sealed class OutputStyleSectionTests : IDisposable {
    private readonly global::Testing.Common.Services.InMemoryFileSystem _fs = new();

    public OutputStyleSectionTests() {
        // 默认 scope：无输出风格 + 注入测试 fs 与双目录（用户级/项目级约定路径）
        PromptConfigSnapshot.SetCurrent(new SystemPromptProviderOptions {
            FileSystem = _fs,
            OutputStyleUserDir = "/user_styles",
            OutputStyleProjectDir = "/project_styles" });
    }

    public void Dispose() => PromptConfigSnapshot.SetCurrent(SystemPromptProviderOptions.Default);

    /// <summary>内建 explanatory — 映射 ExplanatoryStyleSection 内容。</summary>
    [Fact]
    public void GetContent_BuiltinExplanatory_ReturnsExplanatorySection()
    {
        SetOutputStyle("explanatory");

        var content = OutputStyleSection.GetContent();

        content.Should().Be(ExplanatoryStyleSection.GetContent());
    }

    /// <summary>内建 learning — 映射 LearningStyleSection 内容。</summary>
    [Fact]
    public void GetContent_BuiltinLearning_ReturnsLearningSection()
    {
        SetOutputStyle("learning");

        var content = OutputStyleSection.GetContent();

        content.Should().Be(LearningStyleSection.GetContent());
    }

    /// <summary>自定义名 — 用户级目录 .md 内容注入。</summary>
    [Fact]
    public async Task GetContent_CustomStyle_LoadsFromUserDir()
    {
        _fs.WriteAllText("/user_styles/concise.md", "# 简洁风格\n回答保持极简。");
        SetOutputStyle("concise");

        var content = OutputStyleSection.GetContent();

        content.Should().Contain("回答保持极简");
    }

    /// <summary>自定义名 — 项目级同名覆盖用户级（对齐 TS 优先序）。</summary>
    [Fact]
    public async Task GetContent_CustomStyle_ProjectOverridesUser()
    {
        _fs.WriteAllText("/user_styles/concise.md", "用户版");
        _fs.WriteAllText("/project_styles/concise.md", "项目版");
        PromptConfigSnapshot.SetCurrent(new SystemPromptProviderOptions {
            FileSystem = _fs,
            OutputStyle = "concise",
            OutputStyleUserDir = "/user_styles",
            OutputStyleProjectDir = "/project_styles" });

        var content = OutputStyleSection.GetContent();

        content.Should().Contain("项目版");
    }

    /// <summary>未配置 — null（section 不注入）。</summary>
    [Fact]
    public void GetContent_NotConfigured_ReturnsNull()
    {
        OutputStyleSection.GetContent().Should().BeNull();
    }

    /// <summary>自定义名无对应文件 — null 静默降级（不注入不报错）。</summary>
    [Fact]
    public void GetContent_CustomStyleMissingFile_ReturnsNull()
    {
        SetOutputStyle("ghost");

        OutputStyleSection.GetContent().Should().BeNull();
    }

    /// <summary>注入输出风格 — 复用测试类 _fs（文件写入与 Snapshot 同源）。</summary>
    private void SetOutputStyle(string style) =>
        PromptConfigSnapshot.SetCurrent(new SystemPromptProviderOptions {
            FileSystem = _fs,
            OutputStyle = style,
            OutputStyleUserDir = "/user_styles",
            OutputStyleProjectDir = "/project_styles" });
}
