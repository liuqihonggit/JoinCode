namespace JoinCode.CodeIndex.Tests;

public sealed class ProjectIndexTests : IDisposable
{
    private readonly InMemoryIndexStore _store;
    private readonly ProjectIndex _projectIndex;
    private readonly IO.FileSystem.InMemoryFileSystem _fs;

    public ProjectIndexTests()
    {
        _store = new InMemoryIndexStore();
        _fs = new IO.FileSystem.InMemoryFileSystem();
        _projectIndex = new ProjectIndex(_store, _fs);
    }

    public void Dispose()
    {
        _store.Dispose();
    }

    [Fact]
    public async Task IndexProjectAsync_ExistingFile_AddsProject()
    {
        var dir = CreateTempDir();
        var csproj = Path.Combine(dir, "Core.csproj");
        _fs.WriteAllText(csproj,
            """
            <Project>
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <OutputType>Library</OutputType>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="..\Lib\Lib.csproj" />
                <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
              </ItemGroup>
            </Project>
            """);

        await _projectIndex.IndexProjectAsync(csproj, dir, CancellationToken.None).ConfigureAwait(true);

        Assert.Single(_store.Projects);
        Assert.Equal("Core", _store.Projects[csproj].Name);
        Assert.Single(_store.ProjectRefs);
        Assert.Single(_store.NuGetRefs);
    }

    [Fact]
    public async Task IndexProjectAsync_NonExistentFile_DoesNothing()
    {
        await _projectIndex.IndexProjectAsync("missing.csproj", "", CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(_store.Projects);
    }

    [Fact]
    public async Task IndexProjectAsync_NullFilePath_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _projectIndex.IndexProjectAsync(null!, "", CancellationToken.None)).ConfigureAwait(true);
    }

    [Fact]
    public async Task IndexSolutionAsync_SlnFile_IndexesProjects()
    {
        var dir = CreateTempDir();
        var slnPath = Path.Combine(dir, "Test.sln");
        var coreProj = Path.Combine(dir, "Core", "Core.csproj");
        var appProj = Path.Combine(dir, "App", "App.csproj");
        _fs.CreateDirectory(Path.GetDirectoryName(coreProj)!);
        _fs.CreateDirectory(Path.GetDirectoryName(appProj)!);
        _fs.WriteAllText(coreProj, "<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        _fs.WriteAllText(appProj, "<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        _fs.WriteAllText(slnPath,
            """
            Microsoft Visual Studio Solution File, Format Version 12.00
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Core", "Core\Core.csproj", "{A1B2C3D4-1234-5678-90AB-CDEF12345678}"
            EndProject
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "App", "App\App.csproj", "{B2C3D4E5-2345-6789-01BC-DEF23456789A}"
            EndProject
            """);

        await _projectIndex.IndexSolutionAsync(slnPath, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(2, _store.Projects.Count);
        Assert.Contains(_store.Projects, p => p.Value.Name == "Core");
        Assert.Contains(_store.Projects, p => p.Value.Name == "App");
    }

    [Fact]
    public async Task IndexSolutionAsync_SlnxFile_IndexesProjects()
    {
        var dir = CreateTempDir();
        var slnxPath = Path.Combine(dir, "Test.slnx");
        var coreProj = Path.Combine(dir, "Core", "Core.csproj");
        var appProj = Path.Combine(dir, "App", "App.csproj");
        _fs.CreateDirectory(Path.GetDirectoryName(coreProj)!);
        _fs.CreateDirectory(Path.GetDirectoryName(appProj)!);
        _fs.WriteAllText(coreProj, "<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        _fs.WriteAllText(appProj, "<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        _fs.WriteAllText(slnxPath,
            """
            <Solution>
              <Project Path="Core\Core.csproj" Id="A1B2C3D4-1234-5678-90AB-CDEF12345678" />
              <Project Path="App\App.csproj" Id="B2C3D4E5-2345-6789-01BC-DEF23456789A" />
            </Solution>
            """);

        await _projectIndex.IndexSolutionAsync(slnxPath, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(2, _store.Projects.Count);
    }

    [Fact]
    public async Task IndexSolutionAsync_NonExistentFile_DoesNothing()
    {
        await _projectIndex.IndexSolutionAsync("missing.sln", CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(_store.Projects);
    }

    [Fact]
    public async Task IndexSolutionAsync_NullFilePath_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _projectIndex.IndexSolutionAsync(null!, CancellationToken.None)).ConfigureAwait(true);
    }

    [Fact]
    public async Task RemoveProjectAsync_RemovesProjectAndReferences()
    {
        var dir = CreateTempDir();
        var csproj = Path.Combine(dir, "Core.csproj");
        _fs.WriteAllText(csproj,
            """
            <Project>
              <ItemGroup>
                <ProjectReference Include="..\Lib\Lib.csproj" />
                <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
              </ItemGroup>
            </Project>
            """);
        await _projectIndex.IndexProjectAsync(csproj, dir, CancellationToken.None).ConfigureAwait(true);
        Assert.Single(_store.Projects);

        await _projectIndex.RemoveProjectAsync(csproj, CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(_store.Projects);
        Assert.Empty(_store.ProjectRefs);
        Assert.Empty(_store.NuGetRefs);
    }

    [Fact]
    public async Task ClearAsync_RemovesAllProjects()
    {
        var dir = CreateTempDir();
        var csproj = Path.Combine(dir, "Core.csproj");
        _fs.WriteAllText(csproj, "<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        await _projectIndex.IndexProjectAsync(csproj, dir, CancellationToken.None).ConfigureAwait(true);

        await _projectIndex.ClearAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(_store.Projects);
        Assert.Empty(_store.ProjectRefs);
        Assert.Empty(_store.NuGetRefs);
    }

    [Fact]
    public async Task GetProjectCountAsync_ReturnsCount()
    {
        var dir = CreateTempDir();
        var csproj = Path.Combine(dir, "Core.csproj");
        _fs.WriteAllText(csproj, "<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        await _projectIndex.IndexProjectAsync(csproj, dir, CancellationToken.None).ConfigureAwait(true);

        var count = await _projectIndex.GetProjectCountAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(1, count);
    }

    [Fact]
    public void Constructor_NullStore_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ProjectIndex(null!, _fs));
    }

    [Fact]
    public void Constructor_NullFileSystem_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ProjectIndex(_store, null!));
    }

    private string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"projidx_{Guid.NewGuid():N}");
        _fs.CreateDirectory(dir);
        return dir;
    }
}
