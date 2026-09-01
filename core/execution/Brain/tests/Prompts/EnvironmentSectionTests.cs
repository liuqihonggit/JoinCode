namespace Core.Tests.Prompts;

public sealed class EnvironmentSectionTests
{
    [Fact]
    public void GetContent_ShouldContainCurrentDate()
    {
        var fs = new Testing.Common.Services.InMemoryFileSystem();
        fs.SetCurrentDirectory("/test/project");
        PromptConfigSnapshot.SetCurrent(new SystemPromptProviderOptions { FileSystem = fs });

        var content = EnvironmentSection.GetContent();

        content.Should().NotBeNull();
        content.Should().Contain("当前日期");
        content.Should().Contain(DateTime.Now.ToString("yyyy-MM-dd"));
    }

    [Fact]
    public void GetContent_ShouldContainDateBeforeWorkingDirectory()
    {
        var fs = new Testing.Common.Services.InMemoryFileSystem();
        fs.SetCurrentDirectory("/test/project");
        PromptConfigSnapshot.SetCurrent(new SystemPromptProviderOptions { FileSystem = fs });

        var content = EnvironmentSection.GetContent();

        content.Should().NotBeNull();
        content.Should().Contain("当前日期");
        content.Should().Contain("工作目录");
        var dateIndex = content!.IndexOf("当前日期", StringComparison.Ordinal);
        var cwdIndex = content.IndexOf("工作目录", StringComparison.Ordinal);
        dateIndex.Should().BeLessThan(cwdIndex, "当前日期应在工作目录之前，便于 LLM 第一时间感知日期");
    }
}
