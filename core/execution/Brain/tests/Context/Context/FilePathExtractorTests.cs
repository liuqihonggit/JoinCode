namespace Core.Tests.Context;

public sealed class FilePathExtractorTests
{
    [Fact]
    public void ExtractFilePaths_With_Null_Message_Should_Throw_ArgumentNullException()
    {
        var act = () => FilePathExtractor.ExtractFilePaths(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("message");
    }

    [Fact]
    public void ExtractFilePaths_With_Empty_String_Should_Return_Empty()
    {
        var result = FilePathExtractor.ExtractFilePaths("");

        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractFilePaths_With_No_Paths_Should_Return_Empty()
    {
        var result = FilePathExtractor.ExtractFilePaths("请帮我修复这个bug");

        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractFilePaths_With_Windows_Absolute_Path_Should_Extract()
    {
        var result = FilePathExtractor.ExtractFilePaths("请修改 C:\\Projects\\app\\Program.cs 中的代码");

        result.Should().Contain(@"C:\Projects\app\Program.cs");
    }

    [Fact]
    public void ExtractFilePaths_With_Unix_Style_Path_Should_Extract()
    {
        var result = FilePathExtractor.ExtractFilePaths("修改 src/core/Program.cs 文件");

        result.Should().Contain("src/core/Program.cs");
    }

    [Fact]
    public void ExtractFilePaths_With_Bare_Filename_Should_Extract()
    {
        var result = FilePathExtractor.ExtractFilePaths("请查看 ChatService.cs 的实现");

        result.Should().Contain("ChatService.cs");
    }

    [Fact]
    public void ExtractFilePaths_With_Multiple_Paths_Should_Extract_All()
    {
        var result = FilePathExtractor.ExtractFilePaths(
            "修改 Program.cs 和 app.ts 两个文件");

        result.Should().HaveCount(2);
        result.Should().Contain("Program.cs");
        result.Should().Contain("app.ts");
    }

    [Fact]
    public void ExtractFilePaths_Should_Deduplicate_Case_Insensitive()
    {
        var result = FilePathExtractor.ExtractFilePaths(
            "修改 program.cs 和 Program.cs");

        result.Should().HaveCount(1);
    }

    [Fact]
    public void ExtractFilePaths_With_Json_Extension_Should_Extract()
    {
        var result = FilePathExtractor.ExtractFilePaths("查看 appsettings.json 配置");

        result.Should().Contain("appsettings.json");
    }

    [Fact]
    public void ExtractFilePaths_With_Md_Extension_Should_Extract()
    {
        var result = FilePathExtractor.ExtractFilePaths("阅读 README.md 文档");

        result.Should().Contain("README.md");
    }

    [Fact]
    public void ExtractFilePaths_With_Relative_Path_Should_Extract()
    {
        var result = FilePathExtractor.ExtractFilePaths("修改 ./src/app.cs 文件");

        result.Should().Contain("./src/app.cs");
    }

    [Fact]
    public void ExtractFilePaths_With_Parent_Path_Should_Extract()
    {
        var result = FilePathExtractor.ExtractFilePaths("查看 ../shared/utils.cs");

        result.Should().Contain("../shared/utils.cs");
    }

    [Fact]
    public void ExtractFilePaths_With_Tilde_Path_Should_Extract()
    {
        var result = FilePathExtractor.ExtractFilePaths("查看 ~/.jcc/config.json");

        result.Should().Contain("~/.jcc/config.json");
    }

    [Fact]
    public void ExtractFilePaths_With_Sln_Extension_Should_Extract()
    {
        var result = FilePathExtractor.ExtractFilePaths("构建 MyProject.sln 解决方案");

        result.Should().Contain("MyProject.sln");
    }

    [Fact]
    public void ExtractFilePaths_With_Csproj_Extension_Should_Extract()
    {
        var result = FilePathExtractor.ExtractFilePaths("查看 Core.csproj 项目文件");

        result.Should().Contain("Core.csproj");
    }

    [Fact]
    public void ExtractFilePaths_With_Windows_Backslash_Path_Should_Extract()
    {
        var result = FilePathExtractor.ExtractFilePaths(@"打开 D:\Code\src\main.cs");

        result.Should().Contain(@"D:\Code\src\main.cs");
    }

    [Fact]
    public void ExtractFilePaths_With_Yaml_Extension_Should_Extract()
    {
        var result = FilePathExtractor.ExtractFilePaths("编辑 workflow.yaml 配置");

        result.Should().Contain("workflow.yaml");
    }

    [Fact]
    public void ExtractFilePaths_With_Python_Extension_Should_Extract()
    {
        var result = FilePathExtractor.ExtractFilePaths("运行 main.py 脚本");

        result.Should().Contain("main.py");
    }

    [Fact]
    public void ExtractFilePaths_With_Go_Extension_Should_Extract()
    {
        var result = FilePathExtractor.ExtractFilePaths("查看 main.go 入口");

        result.Should().Contain("main.go");
    }

    [Fact]
    public void ExtractFilePaths_With_Rust_Extension_Should_Extract()
    {
        var result = FilePathExtractor.ExtractFilePaths("编译 main.rs 文件");

        result.Should().Contain("main.rs");
    }

    [Fact]
    public void ExtractFilePaths_Should_Not_Extract_Random_Dot_Words()
    {
        var result = FilePathExtractor.ExtractFilePaths("这是一个 test.xyz 文件");

        result.Should().NotContain("test.xyz");
    }

    [Fact]
    public void ExtractFilePaths_With_Mixed_Paths_Should_Extract_All_Types()
    {
        var result = FilePathExtractor.ExtractFilePaths(
            "修改 ./config.json 和 README.md 以及 app.ts");

        result.Should().HaveCountGreaterThanOrEqualTo(3);
        result.Should().Contain("./config.json");
        result.Should().Contain("README.md");
        result.Should().Contain("app.ts");
    }
}
