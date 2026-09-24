namespace JccAuditCli;

/// <summary>
/// 快速项目加载器：用 ProjectCollection 并行加载 csproj + 预编译 DLL 引用 + 直接创建 CSharpCompilation
/// 跳过 MSBuildWorkspace 开销，预计加载时间从 87s 降到 5-10s
/// </summary>
public static class FastProjectLoader {
    private const int MaxConcurrency = 8;

    /// <summary>
    /// 并行加载所有 csproj 并创建 CSharpCompilation
    /// 返回 (项目名, Compilation) 列表
    /// </summary>
    public static async Task<List<(string Name, string FilePath, CSharpCompilation Compilation)>> LoadCompilationsAsync(
        IReadOnlyList<string> projectPaths, CancellationToken ct = default) {
        var loadSw = System.Diagnostics.Stopwatch.StartNew();

        // 阶段1：并行加载 MSBuild 项目（每个 Task 用独立 ProjectCollection，线程安全）
        var msbuildProjects = new ConcurrentBag<(string Path, Microsoft.Build.Evaluation.Project Project)>();
        var loadedCount = 0;
        var semaphore = new SemaphoreSlim(MaxConcurrency);

        var loadTasks = projectPaths.Select(async projectPath => {
            await semaphore.WaitAsync(ct).ConfigureAwait(false);
            try {
                var pc = new Microsoft.Build.Evaluation.ProjectCollection();
                var project = pc.LoadProject(projectPath);
                msbuildProjects.Add((projectPath, project));
                var count = Interlocked.Increment(ref loadedCount);
                if (count % 10 == 0)
                    Console.WriteLine($"  [加载 {count}/{projectPaths.Count}]...");
            } catch (Exception ex) {
                Console.Error.WriteLine($"  加载项目失败: {Path.GetFileName(projectPath)} - {ex.Message}");
            } finally {
                semaphore.Release();
            }
        });

        await Task.WhenAll(loadTasks).ConfigureAwait(false);
        semaphore.Dispose();
        loadSw.Stop();
        Console.WriteLine($"  [统计] MSBuild 项目加载完成 ({msbuildProjects.Count}/{projectPaths.Count})，耗时: {loadSw.Elapsed.TotalSeconds:F1}s");

        // 阶段2：为每个项目创建 CSharpCompilation
        var compileSw = System.Diagnostics.Stopwatch.StartNew();
        var compilations = new ConcurrentBag<(string Name, string FilePath, CSharpCompilation Compilation)>();
        var compiledCount = 0;
        var compileSemaphore = new SemaphoreSlim(MaxConcurrency);

        var compileTasks = msbuildProjects.Select(async item => {
            await compileSemaphore.WaitAsync(ct).ConfigureAwait(false);
            try {
                var compilation = CreateCompilation(item.Project, item.Path);
                if (compilation is not null) {
                    var name = item.Project.GetPropertyValue("AssemblyName") ?? Path.GetFileNameWithoutExtension(item.Path);
                    compilations.Add((name, item.Path, compilation));
                }
                var count = Interlocked.Increment(ref compiledCount);
                if (count % 10 == 0)
                    Console.WriteLine($"  [编译 {count}/{msbuildProjects.Count}]...");
            } catch (Exception ex) {
                Console.Error.WriteLine($"  创建 Compilation 失败: {Path.GetFileName(item.Path)} - {ex.Message}");
                Interlocked.Increment(ref compiledCount);
            } finally {
                compileSemaphore.Release();
            }
        });

        await Task.WhenAll(compileTasks).ConfigureAwait(false);
        compileSemaphore.Dispose();
        compileSw.Stop();
        Console.WriteLine($"  [统计] Compilation 创建完成 ({compilations.Count}/{msbuildProjects.Count})，耗时: {compileSw.Elapsed.TotalSeconds:F1}s");

        return compilations.ToList();
    }

    /// <summary>
    /// 从 MSBuild 项目创建 CSharpCompilation
    /// </summary>
    private static CSharpCompilation? CreateCompilation(Microsoft.Build.Evaluation.Project msbuildProject, string projectPath) {
        var projectDir = Path.GetDirectoryName(projectPath)!;

        // 提取源文件
        var sourceFiles = msbuildProject.GetItems("Compile")
            .Where(i => !i.EvaluatedInclude.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase))
            .Select(i => Path.GetFullPath(Path.Combine(projectDir, i.EvaluatedInclude)))
            .Where(File.Exists)
            .ToList();

        if (sourceFiles.Count == 0) return null;

        // 解析源文件为 SyntaxTree（用 AsParallel 链式）
        var syntaxTrees = sourceFiles
            .AsParallel()
            .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path))
            .ToArray();

        // 提取引用：优先用 .deps.json（完整 transitive 依赖），回退到手动解析
        var metadataReferences = new List<MetadataReference>();
        var nugetRoot = FindNugetRoot(projectDir);

        // 尝试从 .deps.json 加载完整引用
        var depsReferences = LoadReferencesFromDepsJson(msbuildProject, projectDir, nugetRoot);
        if (depsReferences.Count > 0) {
            metadataReferences.AddRange(depsReferences);
        } else {
            // 回退：手动解析 PackageReference + ProjectReference
            foreach (var pkg in msbuildProject.GetItems("PackageReference")) {
                var pkgName = pkg.EvaluatedInclude;
                var pkgVersion = pkg.GetMetadataValue("Version");
                var dllPath = FindPackageDll(nugetRoot, pkgName, pkgVersion);
                if (dllPath is not null) {
                    try { metadataReferences.Add(MetadataReference.CreateFromFile(dllPath)); }
                    catch (Exception ex) { Console.Error.WriteLine($"  加载包引用失败: {pkgName} - {ex.Message}"); }
                }
            }
            foreach (var projRef in msbuildProject.GetItems("ProjectReference")) {
                var refName = Path.GetFileNameWithoutExtension(projRef.EvaluatedInclude);
                var dllPath = FindProjectOutputDll(refName);
                if (dllPath is not null) {
                    try { metadataReferences.Add(MetadataReference.CreateFromFile(dllPath)); }
                    catch (Exception ex) { Console.Error.WriteLine($"  加载项目引用失败: {refName} - {ex.Message}"); }
                }
            }
        }

        // 添加基础引用：所有 .NET 运行时框架 DLL
        var runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        foreach (var frameworkDll in Directory.GetFiles(runtimeDir, "*.dll")) {
            try { metadataReferences.Add(MetadataReference.CreateFromFile(frameworkDll)); }
            catch (Exception ex) { Console.Error.WriteLine($"  加载框架引用失败: {frameworkDll} - {ex.Message}"); }
        }

        // 编译选项
        var nullable = msbuildProject.GetPropertyValue("Nullable");
        var langVersion = msbuildProject.GetPropertyValue("LangVersion");

        var parseOptions = new CSharpParseOptions(
            languageVersion: langVersion == "latest" ? LanguageVersion.Latest : LanguageVersion.Default,
            documentationMode: DocumentationMode.None);

        var compilationOptions = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            nullableContextOptions: nullable == "enable" ? NullableContextOptions.Enable : NullableContextOptions.Disable,
            assemblyIdentityComparer: DesktopAssemblyIdentityComparer.Default);

        var assemblyName = msbuildProject.GetPropertyValue("AssemblyName") ?? Path.GetFileNameWithoutExtension(projectPath);

        return CSharpCompilation.Create(
            assemblyName,
            syntaxTrees,
            metadataReferences,
            compilationOptions);
    }

    /// <summary>
    /// 从 .deps.json 加载完整 transitive 依赖引用
    /// </summary>
    private static List<MetadataReference> LoadReferencesFromDepsJson(
        Microsoft.Build.Evaluation.Project msbuildProject, string projectDir, string? nugetRoot) {
        var references = new List<MetadataReference>();
        var assemblyName = msbuildProject.GetPropertyValue("AssemblyName") ?? Path.GetFileNameWithoutExtension(projectDir);
        var repoRoot = FindRepoRoot();

        var depsPath = FindDepsJson(assemblyName, repoRoot);
        if (depsPath is null || !File.Exists(depsPath)) return references;

        ParseDepsJson(depsPath, nugetRoot, assemblyName, references);

        // 添加 ProjectReference 的预编译 DLL
        foreach (var projRef in msbuildProject.GetItems("ProjectReference")) {
            var refName = Path.GetFileNameWithoutExtension(projRef.EvaluatedInclude);
            var dllPath = FindProjectOutputDll(refName);
            if (dllPath is null) continue;
            try { references.Add(MetadataReference.CreateFromFile(dllPath)); }
            catch (Exception ex) { Console.Error.WriteLine($"  加载项目引用失败: {dllPath} - {ex.Message}"); }
        }

        return references;
    }

    /// <summary>
    /// 解析 .deps.json 的 targets 节点，提取包引用
    /// </summary>
    private static void ParseDepsJson(string depsPath, string? nugetRoot, string assemblyName, List<MetadataReference> references) {
        if (nugetRoot is null) return;
        try {
            var json = File.ReadAllText(depsPath);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("targets", out var targets)) return;

            foreach (var target in targets.EnumerateObject()) {
                ExtractPackageReferences(target.Value, nugetRoot, assemblyName, references);
                break; // 只取第一个 target
            }
        } catch (Exception ex) {
            Console.Error.WriteLine($"  解析 .deps.json 失败: {depsPath} - {ex.Message}");
        }
    }

    /// <summary>
    /// 从 target 节点提取包引用
    /// </summary>
    private static void ExtractPackageReferences(JsonElement targetValue, string nugetRoot, string assemblyName, List<MetadataReference> references) {
        foreach (var pkg in targetValue.EnumerateObject()) {
            var parts = pkg.Name.Split('/');
            var pkgName = parts[0];
            var version = parts.Length > 1 ? parts[1] : "";
            if (pkgName == assemblyName) continue;

            AddRuntimeReferences(pkg.Value, nugetRoot, pkgName, version, "runtime", references);
            AddRuntimeReferences(pkg.Value, nugetRoot, pkgName, version, "compile", references);
        }
    }

    /// <summary>
    /// 添加 runtime/compile 节点中的 DLL 引用
    /// </summary>
    private static void AddRuntimeReferences(JsonElement pkgValue, string nugetRoot, string pkgName, string version, string property, List<MetadataReference> references) {
        if (!pkgValue.TryGetProperty(property, out var runtime)) return;
        foreach (var rt in runtime.EnumerateObject()) {
            var fullPath = Path.Combine(nugetRoot, pkgName.ToLowerInvariant(), version, rt.Name);
            if (!File.Exists(fullPath)) continue;
            try { references.Add(MetadataReference.CreateFromFile(fullPath)); }
            catch (Exception ex) { Console.Error.WriteLine($"  加载引用失败: {fullPath} - {ex.Message}"); }
        }
    }

    /// <summary>
    /// 查找 .deps.json 文件
    /// </summary>
    private static string? FindDepsJson(string assemblyName, string repoRoot) {
        var artifactsBin = Path.Combine(repoRoot, "artifacts", "bin");
        if (!Directory.Exists(artifactsBin)) return null;
        var depsFiles = Directory.GetFiles(artifactsBin, $"{assemblyName}.deps.json", SearchOption.AllDirectories);
        return depsFiles.FirstOrDefault(d => d.Contains("Debug", StringComparison.OrdinalIgnoreCase) &&
                                               d.Contains("net10.0", StringComparison.OrdinalIgnoreCase))
               ?? depsFiles.FirstOrDefault();
    }

    /// <summary>
    /// 查找 nuget 包根目录
    /// </summary>
    private static string? FindNugetRoot(string projectDir) {
        var dir = projectDir;
        while (dir is not null) {
            var nuget = Path.Combine(dir, ".nuget", "packages");
            if (Directory.Exists(nuget)) return nuget;
            dir = Path.GetDirectoryName(dir);
        }
        var globalCache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        return Directory.Exists(globalCache) ? globalCache : null;
    }

    /// <summary>
    /// 在 nuget 缓存中查找包 DLL
    /// </summary>
    private static string? FindPackageDll(string? nugetRoot, string pkgName, string pkgVersion) {
        if (nugetRoot is null) return null;
        var pkgDir = Path.Combine(nugetRoot, pkgName.ToLowerInvariant());
        if (!Directory.Exists(pkgDir)) return null;

        var versionDir = !string.IsNullOrEmpty(pkgVersion)
            ? Path.Combine(pkgDir, pkgVersion)
            : Directory.GetDirectories(pkgDir).OrderByDescending(d => d).FirstOrDefault();

        if (versionDir is null || !Directory.Exists(versionDir)) return null;

        var libDirs = Directory.GetDirectories(versionDir, "lib", SearchOption.AllDirectories);
        foreach (var libDir in libDirs) {
            var tfms = new[] { "net10.0", "net9.0", "net8.0", "netstandard2.0", "netstandard2.1", "net6.0", "net7.0" };
            foreach (var tfm in tfms) {
                var dllPath = Path.Combine(libDir, tfm, $"{pkgName}.dll");
                if (File.Exists(dllPath)) return dllPath;
            }
            var dll = Directory.GetFiles(libDir, "*.dll", SearchOption.AllDirectories)
                .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f) == pkgName);
            if (dll is not null) return dll;
        }

        return null;
    }

    /// <summary>
    /// 在 artifacts/bin 中查找项目输出 DLL（按目录名匹配，不按 DLL 名）
    /// </summary>
    private static string? FindProjectOutputDll(string projectName) {
        var artifactsBin = Path.Combine(FindRepoRoot(), "artifacts", "bin");
        if (!Directory.Exists(artifactsBin)) return null;

        // 优先按目录名匹配：artifacts/bin/{projectName}/Debug/net10.0/*.dll
        var projectBinDir = Path.Combine(artifactsBin, projectName, "Debug", "net10.0");
        if (Directory.Exists(projectBinDir)) {
            // 找主 DLL（与目录名同名或 JoinCode.{projectName}.dll）
            var dlls = Directory.GetFiles(projectBinDir, "*.dll");
            var preferred = dlls.FirstOrDefault(d => Path.GetFileNameWithoutExtension(d) == projectName)
                ?? dlls.FirstOrDefault(d => Path.GetFileNameWithoutExtension(d) == $"JoinCode.{projectName}")
                ?? dlls.FirstOrDefault(d => !Path.GetFileNameWithoutExtension(d).StartsWith("System", StringComparison.OrdinalIgnoreCase));
            if (preferred is not null) return preferred;
        }

        // 回退：全局搜索 {projectName}.dll 或 JoinCode.{projectName}.dll
        var allDlls = Directory.GetFiles(artifactsBin, $"{projectName}.dll", SearchOption.AllDirectories);
        if (allDlls.Length > 0) return allDlls[0];
        var joinCodeDlls = Directory.GetFiles(artifactsBin, $"JoinCode.{projectName}.dll", SearchOption.AllDirectories);
        return joinCodeDlls.FirstOrDefault(d => d.Contains("Debug", StringComparison.OrdinalIgnoreCase) &&
                                                   d.Contains("net10.0", StringComparison.OrdinalIgnoreCase))
               ?? joinCodeDlls.FirstOrDefault();
    }

    /// <summary>
    /// 查找仓库根目录
    /// </summary>
    private static string FindRepoRoot() {
        var dir = AppContext.BaseDirectory;
        while (dir is not null) {
            if (File.Exists(Path.Combine(dir, "Directory.Build.props")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return Directory.GetCurrentDirectory();
    }
}
