namespace Infra.Tests.HotSpot;


public sealed class HotFileDetectorTests
{
    private readonly IHotFileDetector _sut = new HotFileDetector();

    [Theory]
    [InlineData("src/Abstractions/IMailbox.cs", true, "C#接口I*.cs")]
    [InlineData("foundation/Abstractions/00-core/Models/Agent/AgentRole.cs", true, "abstractions目录")]
    [InlineData("contracts/IFooService.cs", true, "contracts目录+接口")]
    [InlineData("src/interfaces/IRepository.cs", true, "interfaces目录")]
    [InlineData("api/Controllers/UserController.cs", true, "api目录")]
    [InlineData("shared/Constants.cs", true, "shared目录+Constant")]
    [InlineData("common/EnumHelper.cs", true, "common目录+Enum")]
    [InlineData("src/models/FooBase.cs", true, "Base命名")]
    [InlineData("src/AbstractHandler.cs", true, "Abstract命名")]
    [InlineData("src/services/FooService.cs", false, "普通C#服务类")]
    [InlineData("src/utils/Helper.cs", false, "普通C#工具类")]
    [InlineData("src/__init__.py", true, "Python模块入口")]
    [InlineData("src/models/__init__.py", true, "Python包入口")]
    [InlineData("src/utils/helper.py", false, "普通Python文件")]
    [InlineData("src/index.ts", true, "JS/TS模块入口")]
    [InlineData("src/index.js", true, "JS模块入口")]
    [InlineData("src/utils/helper.ts", false, "普通TS文件")]
    [InlineData("src/main/java/com/foo/package-info.java", true, "Java包信息")]
    [InlineData("src/main/java/com/foo/UserService.java", false, "普通Java类")]
    [InlineData("src/mod.go", true, "Go模块入口")]
    [InlineData("src/utils/util.go", false, "普通Go文件")]
    [InlineData("settings.json", true, "settings配置")]
    [InlineData("appsettings.json", true, "appsettings配置")]
    [InlineData("config.yaml", true, "yaml配置")]
    [InlineData("foo.toml", true, "toml配置")]
    [InlineData("project.yml", true, "yml配置")]
    [InlineData("src/foo/bar.json", true, "json配置文件")]
    [InlineData("src/Foo.cs", false, "普通C#文件")]
    public void IsHotFile_VariousPaths_ShouldDetectCorrectly(string path, bool expected, string description)
    {
        _sut.IsHotFile(path).Should().Be(expected, description);
    }

    [Theory]
    [InlineData("", "空字符串")]
    [InlineData("   ", "空白")]
    public void IsHotFile_InvalidPath_ShouldReturnFalse(string path, string description)
    {
        _sut.IsHotFile(path).Should().BeFalse(description);
    }

    [Fact]
    public void IsHotFile_WindowsStylePath_ShouldNormalizeAndDetect()
    {
        _sut.IsHotFile(@"foundation\Abstractions\IMailbox.cs").Should().BeTrue("反斜杠路径应归一化后检测");
    }

    [Fact]
    public void DetectHotFiles_MixedPaths_ShouldReturnOnlyHotFiles()
    {
        var paths = new[]
        {
            "src/Abstractions/IFoo.cs",
            "src/utils/helper.ts",
            "src/__init__.py",
            "src/services/FooService.cs",
            "settings.json",
            "src/index.ts"
        };

        var hotFiles = _sut.DetectHotFiles(paths);

        hotFiles.Should().HaveCount(4);
        hotFiles.Should().Contain("src/Abstractions/IFoo.cs");
        hotFiles.Should().Contain("src/__init__.py");
        hotFiles.Should().Contain("settings.json");
        hotFiles.Should().Contain("src/index.ts");
        hotFiles.Should().NotContain("src/utils/helper.ts");
        hotFiles.Should().NotContain("src/services/FooService.cs");
    }

    [Fact]
    public void DetectHotFiles_EmptyInput_ShouldReturnEmptySet()
    {
        _sut.DetectHotFiles([]).Should().BeEmpty();
    }

    [Fact]
    public void IsHotFile_ExtraHotFiles_ShouldMatchConfiguredPaths()
    {
        var sut = new HotFileDetector(
            extraHotFiles: ["src/special/MyService.cs", "custom.config"]);

        sut.IsHotFile("src/special/MyService.cs").Should().BeTrue("额外配置的热文件路径");
        sut.IsHotFile("custom.config").Should().BeTrue("额外配置的热文件名");
        sut.IsHotFile("src/other/Foo.cs").Should().BeFalse("未配置的普通文件");
    }

    [Fact]
    public void IsHotFile_ExtraPatterns_ShouldMatchPatternSubstring()
    {
        var sut = new HotFileDetector(
            extraPatterns: ["Generated", "*.generated.*"]);

        sut.IsHotFile("src/Generated/Foo.cs").Should().BeTrue("路径含Generated模式");
        sut.IsHotFile("src/foo.generated.cs").Should().BeTrue("文件名含generated模式");
        sut.IsHotFile("src/normal/Foo.cs").Should().BeFalse("不含模式");
    }

    [Fact]
    public void IsHotFile_CaseInsensitive_ShouldMatchRegardlessOfCase()
    {
        _sut.IsHotFile("SRC/ABSTRACTIONS/IFOO.CS").Should().BeTrue("大写路径应匹配");
        _sut.IsHotFile("Src/Abstractions/IFoo.Cs").Should().BeTrue("混合大小写应匹配");
    }

    [Fact]
    public void DetectHotFiles_Duplicates_ShouldReturnDistinctSet()
    {
        var paths = new[]
        {
            "src/Abstractions/IFoo.cs",
            "src/Abstractions/IFoo.cs",
            "src/Abstractions/IBar.cs"
        };

        _sut.DetectHotFiles(paths).Should().HaveCount(2);
    }

    [Theory]
    [InlineData("src/bin/IFoo.cs", "bin目录下的接口文件应排除")]
    [InlineData("src/obj/Abstractions/IBar.cs", "obj目录下的接口文件应排除")]
    [InlineData("artifacts/bin/Release/IFoo.cs", "artifacts目录下的接口文件应排除")]
    [InlineData("node_modules/shared/ICommon.cs", "node_modules下的接口文件应排除")]
    [InlineData(".git/config.json", ".git目录下的配置文件应排除")]
    [InlineData(".vs/solution.json", ".vs目录下的配置文件应排除")]
    [InlineData("build/outputs/Constants.java", "build目录下的常量文件应排除")]
    [InlineData("dist/IFoo.cs", "dist目录下的接口文件应排除")]
    [InlineData("target/classes/Constants.class", "target目录应排除")]
    public void IsHotFile_ExcludedDirectories_ShouldReturnFalse(string path, string description)
    {
        _sut.IsHotFile(path).Should().BeFalse(description);
    }

    [Fact]
    public void IsHotFile_SourceDirectoryNotExcluded_ShouldStillDetect()
    {
        _sut.IsHotFile("src/Abstractions/IFoo.cs").Should().BeTrue();
        _sut.IsHotFile("foundation/Contracts/IBar.cs").Should().BeTrue();
    }
}
