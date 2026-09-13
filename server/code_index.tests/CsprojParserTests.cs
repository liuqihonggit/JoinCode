namespace JoinCode.CodeIndex.Tests;

public sealed class CsprojParserTests : IDisposable
{
    private readonly IO.FileSystem.InMemoryFileSystem _fs;

    public CsprojParserTests()
    {
        _fs = new IO.FileSystem.InMemoryFileSystem();
    }

    public void Dispose()
    {
        _fs.Clear();
    }

    [Fact]
    public void Parse_ExtractsProjectName()
    {
        var path = WriteCsproj("<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");

        var result = CsprojParser.Parse(path, _fs, Path.GetDirectoryName(path));

        Assert.Equal("Test", result.Name);
    }

    [Fact]
    public void Parse_ExtractsTargetFramework()
    {
        var path = WriteCsproj("<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");

        var result = CsprojParser.Parse(path, _fs, Path.GetDirectoryName(path));

        Assert.Equal("net10.0", result.TargetFramework);
    }

    [Fact]
    public void Parse_ExtractsOutputType()
    {
        var path = WriteCsproj("<Project><PropertyGroup><OutputType>Exe</OutputType></PropertyGroup></Project>");

        var result = CsprojParser.Parse(path, _fs, Path.GetDirectoryName(path));

        Assert.Equal("Exe", result.OutputType);
    }

    [Fact]
    public void Parse_ExtractsProjectReferences()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"csproj_{Guid.NewGuid():N}");
        _fs.CreateDirectory(dir);
        var path = Path.Combine(dir, "Test.csproj");
        var refPath = Path.Combine("..", "Lib", "Lib.csproj");
        _fs.WriteAllText(path,
            $"<Project><ItemGroup><ProjectReference Include=\"{refPath}\" /></ItemGroup></Project>");

        var result = CsprojParser.Parse(path, _fs, dir);

        Assert.Single(result.ProjectReferences);
        Assert.EndsWith("Lib.csproj", result.ProjectReferences[0]);
    }

    [Fact]
    public void Parse_ExtractsPackageReferences()
    {
        var path = WriteCsproj(
            """
            <Project>
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
                <PackageReference Include="xunit" />
              </ItemGroup>
            </Project>
            """);

        var result = CsprojParser.Parse(path, _fs, Path.GetDirectoryName(path));

        Assert.Equal(2, result.PackageReferences.Count);
        Assert.Contains(result.PackageReferences, p => p.Name == "Newtonsoft.Json" && p.Version == "13.0.3");
        Assert.Contains(result.PackageReferences, p => p.Name == "xunit" && p.Version is null);
    }

    [Fact]
    public void Parse_PackageReferenceWithMsBuildVersion_SetsVersionToNull()
    {
        var path = WriteCsproj(
            """
            <Project>
              <ItemGroup>
                <PackageReference Include="Lib" Version="$(LibVersion)" />
              </ItemGroup>
            </Project>
            """);

        var result = CsprojParser.Parse(path, _fs, Path.GetDirectoryName(path));

        Assert.Single(result.PackageReferences);
        Assert.Null(result.PackageReferences[0].Version);
    }

    [Fact]
    public void Parse_NoProjectReferences_ReturnsEmptyList()
    {
        var path = WriteCsproj("<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");

        var result = CsprojParser.Parse(path, _fs, Path.GetDirectoryName(path));

        Assert.Empty(result.ProjectReferences);
    }

    [Fact]
    public void Parse_ResolvesMsBuildVariablesFromDirectoryBuildProps()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"csproj_{Guid.NewGuid():N}");
        _fs.CreateDirectory(dir);
        var propsPath = Path.Combine(dir, "Directory.Build.props");
        _fs.WriteAllText(propsPath,
            """
            <Project>
              <PropertyGroup>
                <SharedSrcRoot>..\shared</SharedSrcRoot>
              </PropertyGroup>
            </Project>
            """);
        var csprojPath = Path.Combine(dir, "Test.csproj");
        _fs.WriteAllText(csprojPath,
            """
            <Project>
              <ItemGroup>
                <ProjectReference Include="$(SharedSrcRoot)\Lib\Lib.csproj" />
              </ItemGroup>
            </Project>
            """);

        var result = CsprojParser.Parse(csprojPath, _fs, dir);

        Assert.Single(result.ProjectReferences);
        Assert.EndsWith("Lib.csproj", result.ProjectReferences[0]);
        Assert.DoesNotContain("$", result.ProjectReferences[0]);
    }

    [Fact]
    public void Parse_ProjectReferenceWithUnresolvedVariable_IsSkipped()
    {
        var path = WriteCsproj(
            """
            <Project>
              <ItemGroup>
                <ProjectReference Include="$(Unknown)\Lib.csproj" />
              </ItemGroup>
            </Project>
            """);

        var result = CsprojParser.Parse(path, _fs, Path.GetDirectoryName(path));

        Assert.Empty(result.ProjectReferences);
    }

    [Fact]
    public void Parse_NullFilePath_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => CsprojParser.Parse(null!, _fs, ""));
    }

    [Fact]
    public void Parse_NullFileSystem_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => CsprojParser.Parse("test.csproj", null!, ""));
    }

    [Fact]
    public void Parse_ProjectReferenceWithEmptyInclude_IsSkipped()
    {
        var path = WriteCsproj(
            """
            <Project>
              <ItemGroup>
                <ProjectReference Include="" />
              </ItemGroup>
            </Project>
            """);

        var result = CsprojParser.Parse(path, _fs, Path.GetDirectoryName(path));

        Assert.Empty(result.ProjectReferences);
    }

    [Fact]
    public void Parse_PackageReferenceWithEmptyInclude_IsSkipped()
    {
        var path = WriteCsproj(
            """
            <Project>
              <ItemGroup>
                <PackageReference Include="" Version="1.0" />
              </ItemGroup>
            </Project>
            """);

        var result = CsprojParser.Parse(path, _fs, Path.GetDirectoryName(path));

        Assert.Empty(result.PackageReferences);
    }

    private string WriteCsproj(string content)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"csproj_{Guid.NewGuid():N}");
        _fs.CreateDirectory(dir);
        var path = Path.Combine(dir, "Test.csproj");
        _fs.WriteAllText(path, content);
        return path;
    }
}
