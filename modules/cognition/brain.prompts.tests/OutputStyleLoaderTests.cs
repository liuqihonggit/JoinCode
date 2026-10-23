namespace Core.Prompts.Tests;

/// <summary>
/// 输出风格加载器测试 — 对齐 TS loadOutputStylesDir.ts：双层目录（用户 ~/.jcc/output-styles/
/// + 项目 {cwd}/.jcc/output-styles/，项目覆盖用户同名额）扫描 *.md → 文件名即样式名、
/// 内容即 prompt；缺目录空清单；非 .md 文件忽略。
/// </summary>
public sealed class OutputStyleLoaderTests
{
    [Fact]
    public async Task LoadAsync_用户目录Markdown_文件名为样式名内容为Prompt()
    {
        var fs = new global::Testing.Common.Services.InMemoryFileSystem();
        await fs.WriteAllTextAsync("/user/concise.md", "# 简洁风格\n回答保持极简。");
        var sut = new OutputStyleLoader(fs, "/user", "/project_missing");

        var styles = await sut.LoadAsync();

        var style = styles.Should().ContainSingle().Which;
        style.Name.Should().Be("concise");
        style.Prompt.Should().Contain("回答保持极简");
        style.Source.Should().Be(OutputStyleSource.UserSettings);
    }

    [Fact]
    public async Task LoadAsync_项目目录同名_覆盖用户级()
    {
        var fs = new global::Testing.Common.Services.InMemoryFileSystem();
        await fs.WriteAllTextAsync("/user/concise.md", "用户版内容");
        await fs.WriteAllTextAsync("/project/concise.md", "项目版内容");
        await fs.WriteAllTextAsync("/project/extra.md", "项目独有");
        var sut = new OutputStyleLoader(fs, "/user", "/project");

        var styles = await sut.LoadAsync();

        styles.Should().HaveCount(2);
        styles.Single(s => s.Name == "concise").Prompt.Should().Contain("项目版内容");
        styles.Single(s => s.Name == "concise").Source.Should().Be(OutputStyleSource.ProjectSettings);
        styles.Single(s => s.Name == "extra").Source.Should().Be(OutputStyleSource.ProjectSettings);
    }

    [Fact]
    public async Task LoadAsync_两目录均缺_返回空清单()
    {
        var fs = new global::Testing.Common.Services.InMemoryFileSystem();
        var sut = new OutputStyleLoader(fs, "/nope_user", "/nope_project");

        var styles = await sut.LoadAsync();

        styles.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadAsync_非Markdown文件_忽略()
    {
        var fs = new global::Testing.Common.Services.InMemoryFileSystem();
        await fs.WriteAllTextAsync("/user/notes.txt", "不是样式");
        await fs.WriteAllTextAsync("/user/real.md", "真样式");
        var sut = new OutputStyleLoader(fs, "/user", "/nope");

        var styles = await sut.LoadAsync();

        styles.Should().ContainSingle().Which.Name.Should().Be("real");
    }
}
