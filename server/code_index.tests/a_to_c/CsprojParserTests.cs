// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003
namespace JoinCode.CodeIndex.Tests;

public sealed class CsprojParserTests : IDisposable {
    private readonly IO.FileSystem.InMemoryFileSystem _fs;
    private bool _disposed;

    public CsprojParserTests() {
        _fs = new IO.FileSystem.InMemoryFileSystem();
    }

    public void Dispose() {
        if (_disposed) return;
        _disposed = true;
        _fs.Clear();
    }

    [Fact]
    public async Task Parse_ExtractsProjectName() {
        var path = await WriteCsproj("<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");

        var result = await CsprojParser.ParseAsync(path, _fs, Path.GetDirectoryName(path));

        Assert.Equal("Test", result.Name);
    }

    [Fact]
    public async Task Parse_ExtractsTargetFramework() {
        var path = await WriteCsproj("<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");

        var result = await CsprojParser.ParseAsync(path, _fs, Path.GetDirectoryName(path));

        Assert.Equal("net10.0", result.TargetFramework);
    }

    [Fact]
    public async Task Parse_ExtractsOutputType() {
        var path = await WriteCsproj("<Project><PropertyGroup><OutputType>Exe</OutputType></PropertyGroup></Project>");

        var result = await CsprojParser.ParseAsync(path, _fs, Path.GetDirectoryName(path));

        Assert.Equal("Exe", result.OutputType);
    }

    [Fact]
    public async Task Parse_ExtractsProjectReferences() {
        var dir = Path.Combine(Path.GetTempPath(), $"csproj_{Guid.NewGuid():N}");
        _fs.CreateDirectory(dir);
        var path = Path.Combine(dir, "Test.csproj");
        var refPath = Path.Combine("..", "Lib", "Lib.csproj");
        await _fs.WriteAllText(path,
            $"<Project><ItemGroup><ProjectReference Include=\"{refPath}\" /></ItemGroup></Project>");

        var result = await CsprojParser.ParseAsync(path, _fs, dir);

        Assert.Single(result.ProjectReferences);
        Assert.EndsWith("Lib.csproj", result.ProjectReferences[0]);
    }

    [Fact]
    public async Task Parse_ExtractsPackageReferences() {
        var path = await WriteCsproj(
            """
            <Project>
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
                <PackageReference Include="xunit" />
              </ItemGroup>
            </Project>
            """);

        var result = await CsprojParser.ParseAsync(path, _fs, Path.GetDirectoryName(path));

        Assert.Equal(2, result.PackageReferences.Count);
        Assert.Contains(result.PackageReferences, p => p.Name == "Newtonsoft.Json" && p.Version == "13.0.3");
        Assert.Contains(result.PackageReferences, p => p.Name == "xunit" && p.Version is null);
    }

    [Fact]
    public async Task Parse_PackageReferenceWithMsBuildVersion_SetsVersionToNull() {
        var path = await WriteCsproj(
            """
            <Project>
              <ItemGroup>
                <PackageReference Include="Lib" Version="$(LibVersion)" />
              </ItemGroup>
            </Project>
            """);

        var result = await CsprojParser.ParseAsync(path, _fs, Path.GetDirectoryName(path));

        Assert.Single(result.PackageReferences);
        Assert.Null(result.PackageReferences[0].Version);
    }

    [Fact]
    public async Task Parse_NoProjectReferences_ReturnsEmptyList() {
        var path = await WriteCsproj("<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");

        var result = await CsprojParser.ParseAsync(path, _fs, Path.GetDirectoryName(path));

        Assert.Empty(result.ProjectReferences);
    }

    [Fact]
    public async Task Parse_ResolvesMsBuildVariablesFromDirectoryBuildProps() {
        var dir = Path.Combine(Path.GetTempPath(), $"csproj_{Guid.NewGuid():N}");
        _fs.CreateDirectory(dir);
        var propsPath = Path.Combine(dir, "Directory.Build.props");
        await _fs.WriteAllText(propsPath,
            """
            <Project>
              <PropertyGroup>
                <SharedSrcRoot>..\shared</SharedSrcRoot>
              </PropertyGroup>
            </Project>
            """);
        var csprojPath = Path.Combine(dir, "Test.csproj");
        await _fs.WriteAllText(csprojPath,
            """
            <Project>
              <ItemGroup>
                <ProjectReference Include="$(SharedSrcRoot)\Lib\Lib.csproj" />
              </ItemGroup>
            </Project>
            """);

        var result = await CsprojParser.ParseAsync(csprojPath, _fs, dir);

        Assert.Single(result.ProjectReferences);
        Assert.EndsWith("Lib.csproj", result.ProjectReferences[0]);
        Assert.DoesNotContain("$", result.ProjectReferences[0]);
    }

    [Fact]
    public async Task Parse_ProjectReferenceWithUnresolvedVariable_IsSkipped() {
        var path = await WriteCsproj(
            """
            <Project>
              <ItemGroup>
                <ProjectReference Include="$(Unknown)\Lib.csproj" />
              </ItemGroup>
            </Project>
            """);

        var result = await CsprojParser.ParseAsync(path, _fs, Path.GetDirectoryName(path));

        Assert.Empty(result.ProjectReferences);
    }

    [Fact]
    public async Task Parse_NullFilePath_Throws() {
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await CsprojParser.ParseAsync(null!, _fs, ""));
    }

    [Fact]
    public async Task Parse_NullFileSystem_Throws() {
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await CsprojParser.ParseAsync("test.csproj", null!, ""));
    }

    [Fact]
    public async Task Parse_ProjectReferenceWithEmptyInclude_IsSkipped() {
        var path = await WriteCsproj(
            """
            <Project>
              <ItemGroup>
                <ProjectReference Include="" />
              </ItemGroup>
            </Project>
            """);

        var result = await CsprojParser.ParseAsync(path, _fs, Path.GetDirectoryName(path));

        Assert.Empty(result.ProjectReferences);
    }

    [Fact]
    public async Task Parse_PackageReferenceWithEmptyInclude_IsSkipped() {
        var path = await WriteCsproj(
            """
            <Project>
              <ItemGroup>
                <PackageReference Include="" Version="1.0" />
              </ItemGroup>
            </Project>
            """);

        var result = await CsprojParser.ParseAsync(path, _fs, Path.GetDirectoryName(path));

        Assert.Empty(result.PackageReferences);
    }

    private async Task<string > WriteCsproj(string content) {
        var dir = Path.Combine(Path.GetTempPath(), $"csproj_{Guid.NewGuid():N}");
        _fs.CreateDirectory(dir);
        var path = Path.Combine(dir, "Test.csproj");
        await _fs.WriteAllText(path, content);
        return path;
    }

    // ============ ReplaceMsBuildVariables 确定性测试 ============

    [Fact]
    public void ReplaceMsBuildVariables_SingleVariable_ReplacesAll() {
        var props = new Dictionary<string, string> { ["SharedRoot"] = "..\\shared" };
        var result = CsprojParser.ReplaceMsBuildVariables("$(SharedRoot)\\Lib.csproj", props);
        Assert.Equal("..\\shared\\Lib.csproj", result);
    }

    [Fact]
    public void ReplaceMsBuildVariables_MultipleVariables_ReplacesAll() {
        var props = new Dictionary<string, string> { ["A"] = "alpha", ["B"] = "beta" };
        var result = CsprojParser.ReplaceMsBuildVariables("$(A)_$(B)", props);
        Assert.Equal("alpha_beta", result);
    }

    [Fact]
    public void ReplaceMsBuildVariables_NestedVariable_ResolvesIteratively() {
        var props = new Dictionary<string, string> { ["Outer"] = "$(Inner)", ["Inner"] = "resolved" };
        var result = CsprojParser.ReplaceMsBuildVariables("$(Outer)", props);
        Assert.Equal("resolved", result);
    }

    [Fact]
    public void ReplaceMsBuildVariables_NoMatch_ReturnsOriginal() {
        var props = new Dictionary<string, string> { ["A"] = "x" };
        var result = CsprojParser.ReplaceMsBuildVariables("no_vars_here", props);
        Assert.Equal("no_vars_here", result);
    }

    [Fact]
    public void ReplaceMsBuildVariables_EmptyProps_ReturnsOriginal() {
        var result = CsprojParser.ReplaceMsBuildVariables("$(A)", []);
        Assert.Equal("$(A)", result);
    }

    [Fact]
    public void ReplaceMsBuildVariables_CyclicVariables_StopsAtMaxIterations() {
        // 循环引用 A→B→A,应在 10 次迭代后停止(不无限循环)
        var props = new Dictionary<string, string> { ["A"] = "$(B)", ["B"] = "$(A)" };
        var result = CsprojParser.ReplaceMsBuildVariables("$(A)", props);
        // 不会完全解析,但不抛异常
        Assert.NotNull(result);
    }

    // ============ NormalizePath 确定性测试 ============

    [Theory]
    [InlineData("a/b/c", "a\\b\\c")]
    [InlineData("a\\b\\c", "a\\b\\c")]
    [InlineData("a/b\\c/d", "a\\b\\c\\d")]
    [InlineData("single", "single")]
    public void NormalizePath_UnifiesSeparators(string input, string expected) {
        Assert.Equal(expected, CsprojParser.NormalizePath(input));
    }
}