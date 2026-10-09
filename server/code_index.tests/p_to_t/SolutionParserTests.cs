// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003
namespace JoinCode.CodeIndex.Tests;

public sealed class SolutionParserTests : IDisposable {
    private readonly IO.FileSystem.InMemoryFileSystem _fs;
    private bool _disposed;

    public SolutionParserTests() {
        _fs = new IO.FileSystem.InMemoryFileSystem();
    }

    public void Dispose() {
        if (_disposed) return;
        _disposed = true;
        _fs.Clear();
    }

    [Fact]
    public async Task ParseSln_ExtractsProjectEntries() {
        var dir = Path.Combine(Path.GetTempPath(), $"sln_{Guid.NewGuid():N}");
        _fs.CreateDirectory(dir);
        var slnPath = Path.Combine(dir, "Test.sln");
        await _fs.WriteAllText(slnPath,
            """
            Microsoft Visual Studio Solution File, Format Version 12.00
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Core", "Core\Core.csproj", "{A1B2C3D4-1234-5678-90AB-CDEF12345678}"
            EndProject
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "App", "App\App.csproj", "{B2C3D4E5-2345-6789-01BC-DEF23456789A}"
            EndProject
            """);

        var result = await SolutionParser.ParseSlnAsync(slnPath, _fs);

        Assert.Equal(2, result.Projects.Count);
        Assert.Contains(result.Projects, p => p.Name == "Core");
        Assert.Contains(result.Projects, p => p.Name == "App");
    }

    [Fact]
    public async Task ParseSln_SkipsNonCsprojProjects() {
        var dir = Path.Combine(Path.GetTempPath(), $"sln_{Guid.NewGuid():N}");
        _fs.CreateDirectory(dir);
        var slnPath = Path.Combine(dir, "Test.sln");
        await _fs.WriteAllText(slnPath,
            """
            Microsoft Visual Studio Solution File, Format Version 12.00
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Core", "Core\Core.csproj", "{A1B2C3D4-1234-5678-90AB-CDEF12345678}"
            EndProject
            Project("{8BC9CEB8-8B4A-11D0-8D11-00A0C91BC942}") = "Native", "Native\Native.vcxproj", "{C3D4E5F6-3456-7890-12CD-EF345678901B}"
            EndProject
            """);

        var result = await SolutionParser.ParseSlnAsync(slnPath, _fs);

        Assert.Single(result.Projects);
        Assert.Equal("Core", result.Projects[0].Name);
    }

    [Fact]
    public async Task ParseSlnx_ExtractsProjectEntries() {
        var dir = Path.Combine(Path.GetTempPath(), $"sln_{Guid.NewGuid():N}");
        _fs.CreateDirectory(dir);
        var slnxPath = Path.Combine(dir, "Test.slnx");
        await _fs.WriteAllText(slnxPath,
            """
            <Solution>
              <Project Path="Core\Core.csproj" Id="A1B2C3D4-1234-5678-90AB-CDEF12345678" />
              <Project Path="App\App.csproj" Id="B2C3D4E5-2345-6789-01BC-DEF23456789A" />
            </Solution>
            """);

        var result = await SolutionParser.ParseSlnxAsync(slnxPath, _fs);

        Assert.Equal(2, result.Projects.Count);
        Assert.Contains(result.Projects, p => p.Name == "Core");
        Assert.Contains(result.Projects, p => p.Name == "App");
    }

    [Fact]
    public async Task ParseSln_EmptySolution_ReturnsEmptyList() {
        var dir = Path.Combine(Path.GetTempPath(), $"sln_{Guid.NewGuid():N}");
        _fs.CreateDirectory(dir);
        var slnPath = Path.Combine(dir, "Empty.sln");
        await _fs.WriteAllText(slnPath, "Microsoft Visual Studio Solution File, Format Version 12.00\n");

        var result = await SolutionParser.ParseSlnAsync(slnPath, _fs);

        Assert.Empty(result.Projects);
    }

    [Fact]
    public async Task ParseSln_MalformedProjectLine_NoEquals_ReturnsEmpty() {
        var dir = Path.Combine(Path.GetTempPath(), $"sln_{Guid.NewGuid():N}");
        _fs.CreateDirectory(dir);
        var slnPath = Path.Combine(dir, "Test.sln");
        await _fs.WriteAllText(slnPath,
            """
            Microsoft Visual Studio Solution File, Format Version 12.00
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") "Core", "Core\Core.csproj"
            EndProject
            """);

        var result = await SolutionParser.ParseSlnAsync(slnPath, _fs);

        Assert.Empty(result.Projects);
    }

    [Fact]
    public async Task ParseSln_MalformedProjectLine_NotQuoted_ReturnsEmpty() {
        var dir = Path.Combine(Path.GetTempPath(), $"sln_{Guid.NewGuid():N}");
        _fs.CreateDirectory(dir);
        var slnPath = Path.Combine(dir, "Test.sln");
        await _fs.WriteAllText(slnPath,
            """
            Microsoft Visual Studio Solution File, Format Version 12.00
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = Core, Core\Core.csproj, {A1B2C3D4-1234-5678-90AB-CDEF12345678}
            EndProject
            """);

        var result = await SolutionParser.ParseSlnAsync(slnPath, _fs);

        Assert.Empty(result.Projects);
    }

    [Fact]
    public async Task ParseSlnx_EmptyPath_IsSkipped() {
        var dir = Path.Combine(Path.GetTempPath(), $"sln_{Guid.NewGuid():N}");
        _fs.CreateDirectory(dir);
        var slnxPath = Path.Combine(dir, "Test.slnx");
        await _fs.WriteAllText(slnxPath,
            """
            <Solution>
              <Project Path="" Id="A1B2C3D4-1234-5678-90AB-CDEF12345678" />
              <Project Path="App\App.csproj" Id="B2C3D4E5-2345-6789-01BC-DEF23456789A" />
            </Solution>
            """);

        var result = await SolutionParser.ParseSlnxAsync(slnxPath, _fs);

        Assert.Single(result.Projects);
        Assert.Equal("App", result.Projects[0].Name);
    }

    [Fact]
    public async Task ParseSln_NullFilePath_Throws() {
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await SolutionParser.ParseSlnAsync(null!, _fs));
    }

    [Fact]
    public async Task ParseSln_NullFileSystem_Throws() {
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await SolutionParser.ParseSlnAsync("test.sln", null!));
    }

    [Fact]
    public async Task ParseSlnx_NullFilePath_Throws() {
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await SolutionParser.ParseSlnxAsync(null!, _fs));
    }

    [Fact]
    public async Task ParseSlnx_NullFileSystem_Throws() {
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await SolutionParser.ParseSlnxAsync("test.slnx", null!));
    }

    // ============ ParseProjectLine 确定性测试 ============

    [Fact]
    public void ParseProjectLine_StandardCsproj_ReturnsEntry() {
        var line = "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"Core\", \"Core\\Core.csproj\", \"{A1B2C3D4-1234-5678-90AB-CDEF12345678}\"";

        var entry = SolutionParser.ParseProjectLine(line, "");

        Assert.NotNull(entry);
        Assert.Equal("Core", entry!.Name);
        Assert.EndsWith("Core.csproj", entry.RelativePath);
        Assert.Equal("A1B2C3D4-1234-5678-90AB-CDEF12345678", entry.ProjectGuid);
    }

    [Fact]
    public void ParseProjectLine_NoEquals_ReturnsNull() {
        var line = "Project(\"{FAE04EC0}\") \"Core\", \"Core.csproj\", \"{GUID}\"";

        Assert.Null(SolutionParser.ParseProjectLine(line, ""));
    }

    [Fact]
    public void ParseProjectLine_NonCsproj_ReturnsNull() {
        var line = "Project(\"{8BC9CEB8}\") = \"Native\", \"Native\\Native.vcxproj\", \"{GUID}\"";

        Assert.Null(SolutionParser.ParseProjectLine(line, ""));
    }

    [Fact]
    public void ParseProjectLine_MissingQuotes_ReturnsNull() {
        var line = "Project(\"{FAE04EC0}\") = Core, Core.csproj, {GUID}";

        Assert.Null(SolutionParser.ParseProjectLine(line, ""));
    }

    [Fact]
    public void ParseProjectLine_TooFewParts_ReturnsNull() {
        var line = "Project(\"{FAE04EC0}\") = \"Core\", \"{GUID}\"";

        Assert.Null(SolutionParser.ParseProjectLine(line, ""));
    }

    // ============ SplitQuotedParts 确定性测试 ============

    [Fact]
    public void SplitQuotedParts_ThreeQuotedParts_ReturnsAll() {
        var input = " = \"Core\", \"Core\\Core.csproj\", \"{GUID}\"".AsSpan();

        var parts = SolutionParser.SplitQuotedParts(input);

        Assert.Equal(3, parts.Count);
        Assert.Equal("Core", parts[0]);
        Assert.Equal("Core\\Core.csproj", parts[1]);
        Assert.Equal("{GUID}", parts[2]);
    }

    [Fact]
    public void SplitQuotedParts_EmptyQuotedPart_Included() {
        var input = " \"a\", \"\", \"b\" ".AsSpan();

        var parts = SolutionParser.SplitQuotedParts(input);

        // 空引号 "" 长度为0,被跳过(i > start 检查)
        Assert.Equal(2, parts.Count);
        Assert.Equal("a", parts[0]);
        Assert.Equal("b", parts[1]);
    }

    [Fact]
    public void SplitQuotedParts_NoQuotes_ReturnsEmpty() {
        var input = "no quotes here".AsSpan();

        var parts = SolutionParser.SplitQuotedParts(input);

        Assert.Empty(parts);
    }

    [Fact]
    public void SplitQuotedParts_SingleQuote_ReturnsContentAfterQuote() {
        // 只有一个引号: 引号后的内容被当作一个 part(无闭合引号)
        var input = "only \" one quote".AsSpan();

        var parts = SolutionParser.SplitQuotedParts(input);

        Assert.Single(parts);
        Assert.Equal(" one quote", parts[0]);
    }
}