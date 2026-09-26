namespace JoinCode.CodeIndex.Tests;

public sealed class ProjectDependencyGraphTests : IDisposable {
    private readonly InMemoryIndexStore _store;
    private readonly ProjectDependencyGraph _graph;
    private bool _disposed;

    public ProjectDependencyGraphTests() {
        _store = new InMemoryIndexStore();
        _graph = new ProjectDependencyGraph(_store);
    }

    public void Dispose() {
        if (_disposed) return;
        _disposed = true;
        _store.Dispose();
    }

    [Fact]
    public async Task GetProjectDependenciesAsync_ReturnsDependencies() {
        InsertProject("Core.csproj", "Core");
        InsertProject("JoinCode.Abstractions.csproj", "Contracts");
        InsertProjectRef("Core.csproj", "JoinCode.Abstractions.csproj");

        var deps = await _graph.GetProjectDependenciesAsync("Core.csproj", CancellationToken.None).ConfigureAwait(true);

        Assert.Single(deps);
        Assert.Equal("Core.csproj", deps[0].SourceProjectPath);
        Assert.Equal("JoinCode.Abstractions.csproj", deps[0].TargetProjectPath);
    }

    [Fact]
    public async Task GetProjectDependentsAsync_ReturnsDependents() {
        InsertProject("Core.csproj", "Core");
        InsertProject("JoinCode.Abstractions.csproj", "Contracts");
        InsertProjectRef("Core.csproj", "JoinCode.Abstractions.csproj");

        var dependents = await _graph.GetProjectDependentsAsync("JoinCode.Abstractions.csproj", CancellationToken.None).ConfigureAwait(true);

        Assert.Single(dependents);
        Assert.Equal("Core.csproj", dependents[0].SourceProjectPath);
    }

    [Fact]
    public async Task GetProjectDependenciesAsync_NoDependencies_ReturnsEmpty() {
        var deps = await _graph.GetProjectDependenciesAsync("NonExistent.csproj", CancellationToken.None).ConfigureAwait(true);
        Assert.Empty(deps);
    }

    [Fact]
    public async Task GetAffectedProjectsAsync_ReturnsUpstreamProjects() {
        InsertProject("JoinCode.Abstractions.csproj", "Contracts");
        InsertProject("Core.csproj", "Core");
        InsertProject("App.csproj", "App");
        InsertProjectRef("Core.csproj", "JoinCode.Abstractions.csproj");
        InsertProjectRef("App.csproj", "Core.csproj");

        var affected = await _graph.GetAffectedProjectsAsync("JoinCode.Abstractions.csproj", CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(2, affected.Count);
        Assert.Contains(affected, p => p.EndsWith("Core.csproj"));
        Assert.Contains(affected, p => p.EndsWith("App.csproj"));
    }

    [Fact]
    public async Task GetProjectNuGetPackagesAsync_ReturnsPackages() {
        InsertProject("Core.csproj", "Core");
        InsertNuGetRef("Core.csproj", "Microsoft.Data.Sqlite", "10.0.0");
        InsertNuGetRef("Core.csproj", "TreeSitter.DotNet", "1.3.0");

        var packages = await _graph.GetProjectNuGetPackagesAsync("Core.csproj", CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(2, packages.Count);
        Assert.Contains(packages, p => p.PackageName == "Microsoft.Data.Sqlite");
        Assert.Contains(packages, p => p.PackageName == "TreeSitter.DotNet");
    }

    [Fact]
    public async Task GetProjectsUsingNuGetPackageAsync_ReturnsProjects() {
        InsertProject("Core.csproj", "Core");
        InsertProject("App.csproj", "App");
        InsertNuGetRef("Core.csproj", "Microsoft.Data.Sqlite", "10.0.0");
        InsertNuGetRef("App.csproj", "Microsoft.Data.Sqlite", "10.0.0");

        var projects = await _graph.GetProjectsUsingNuGetPackageAsync("Microsoft.Data.Sqlite", CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(2, projects.Count);
    }

    [Fact]
    public async Task GetAllProjectsAsync_ReturnsAllProjects() {
        InsertProject("Core.csproj", "Core", "net10.0", "Library");
        InsertProject("App.csproj", "App", "net10.0", "Exe");

        var projects = await _graph.GetAllProjectsAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(2, projects.Count);
        Assert.Contains(projects, p => p.Name == "Core" && p.TargetFramework == "net10.0");
        Assert.Contains(projects, p => p.Name == "App" && p.OutputType == "Exe");
    }

    [Fact]
    public async Task InvalidateCache_RefreshesData() {
        var projects = await _graph.GetAllProjectsAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.Empty(projects);

        InsertProject("New.csproj", "New");

        _graph.InvalidateCache();

        projects = await _graph.GetAllProjectsAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.Single(projects);
    }

    [Fact]
    public async Task GetAffectedProjectsAsync_CircularDependency_DoesNotHang() {
        InsertProject("projectA.csproj", "ProjectA");
        InsertProject("projectB.csproj", "ProjectB");
        InsertProjectRef("projectA.csproj", "projectB.csproj");
        InsertProjectRef("projectB.csproj", "projectA.csproj");

        var affected = await _graph.GetAffectedProjectsAsync("projectA.csproj", CancellationToken.None).ConfigureAwait(true);

        Assert.Single(affected);
        Assert.Contains(affected, p => p.EndsWith("projectB.csproj"));
    }

    [Fact]
    public async Task GetAffectedProjectsAsync_DiamondDependency_NoDuplicates() {
        InsertProject("A.csproj", "A");
        InsertProject("B.csproj", "B");
        InsertProject("C.csproj", "C");
        InsertProject("D.csproj", "D");
        InsertProjectRef("A.csproj", "B.csproj");
        InsertProjectRef("A.csproj", "C.csproj");
        InsertProjectRef("B.csproj", "D.csproj");
        InsertProjectRef("C.csproj", "D.csproj");

        var affected = await _graph.GetAffectedProjectsAsync("D.csproj", CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(3, affected.Count);
        Assert.Single(affected, p => p.EndsWith("A.csproj"));
    }

    [Fact]
    public async Task FindOwningProjectAsync_FileInSubdirectory_ReturnsProject() {
        InsertProject("src/Core/Core.csproj", "Core");

        var result = await _graph.FindOwningProjectAsync("src/Core/Services/MyService.cs", CancellationToken.None).ConfigureAwait(true);

        Assert.NotNull(result);
        Assert.Equal("src/Core/Core.csproj", result);
    }

    private void InsertProject(string filePath, string name, string? tfm = null, string? outputType = null) {
        var info = new ProjectInfo {
            Name = name,
            FilePath = filePath,
            TargetFramework = tfm,
            OutputType = outputType
        };
        _store.Update(snap => snap with { Projects = snap.Projects.SetItem(filePath, info) });
    }

    private void InsertProjectRef(string source, string target) {
        var edge = new ProjectReferenceEdge {
            SourceProjectPath = source,
            TargetProjectPath = target
        };
        var normTarget = InMemoryIndexStore.NormalizeKey(target);
        _store.Update(snap => {
            var projectRefs = snap.ProjectRefs;
            if (!projectRefs.TryGetValue(source, out var list)) {
                list = ImmutableList<ProjectReferenceEdge>.Empty;
            }
            projectRefs = projectRefs.SetItem(source, list.Add(edge));

            var byTarget = snap.ProjectRefsByTarget;
            if (!byTarget.TryGetValue(normTarget, out var targetList)) {
                targetList = ImmutableList<ProjectReferenceEdge>.Empty;
            }
            byTarget = byTarget.SetItem(normTarget, targetList.Add(edge));

            return snap with { ProjectRefs = projectRefs, ProjectRefsByTarget = byTarget };
        });
    }

    private void InsertNuGetRef(string projectPath, string packageName, string? version = null) {
        var pkg = new NuGetPackageReference {
            ProjectPath = projectPath,
            PackageName = packageName,
            Version = version
        };
        _store.Update(snap => {
            var nuGetRefs = snap.NuGetRefs;
            if (!nuGetRefs.TryGetValue(projectPath, out var list)) {
                list = ImmutableList<NuGetPackageReference>.Empty;
            }
            nuGetRefs = nuGetRefs.SetItem(projectPath, list.Add(pkg));

            var byPackage = snap.NuGetRefsByPackage;
            if (!byPackage.TryGetValue(packageName, out var pkgList)) {
                pkgList = ImmutableList<NuGetPackageReference>.Empty;
            }
            byPackage = byPackage.SetItem(packageName, pkgList.Add(pkg));

            return snap with { NuGetRefs = nuGetRefs, NuGetRefsByPackage = byPackage };
        });
    }
}