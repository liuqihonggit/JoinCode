namespace JccAuditCli;

/// <summary>
/// 项目审计引擎：编译项目并收集诊断
/// </summary>
public sealed class AuditEngine
{
    private readonly List<DiagnosticAnalyzer> _analyzers;
    private const int MaxConcurrency = 8;

    public AuditEngine(List<DiagnosticAnalyzer> analyzers)
    {
        _analyzers = analyzers;
    }

    /// <summary>
    /// 审计解决方案中的所有项目
    /// 支持 .sln（MSBuildWorkspace 原生）和 .slnx（手动解析后逐个加载）
    /// </summary>
    public async Task<AuditReport> AuditSolutionAsync(string solutionPath, bool skipTests = false, CancellationToken ct = default)
    {
        var totalSw = System.Diagnostics.Stopwatch.StartNew();
        Console.WriteLine($"正在加载解决方案: {solutionPath}");

        var ext = Path.GetExtension(solutionPath).ToLowerInvariant();

        if (ext == ".slnx")
        {
            return await AuditSlnxAsync(solutionPath, skipTests, ct);
        }

        // .sln 格式：使用 MSBuildWorkspace 原生加载
        var msbuildProps = CreateMsBuildProps();

        using var workspace = MSBuildWorkspace.Create(msbuildProps);
        workspace.SkipUnrecognizedProjects = true;
        workspace.LoadMetadataForReferencedProjects = false;

        var solution = await workspace.OpenSolutionAsync(solutionPath, cancellationToken: ct);

        if (workspace.Diagnostics.Count > 0)
        {
            foreach (var diag in workspace.Diagnostics.Where(d => d.Kind == WorkspaceDiagnosticKind.Failure))
            {
                Console.Error.WriteLine($"  工作区警告: {diag.Message}");
            }
        }

        // 过滤并并行处理项目
        var projects = solution.Projects
            .Where(p => !p.Name.Contains("Generator", StringComparison.Ordinal))
            .Where(p => !skipTests || !IsTestProject(p.Name))
            .ToList();

        var projectResults = await ProcessProjectsParallelAsync(projects, ct);

        totalSw.Stop();
        Console.WriteLine($"审计完成，耗时: {totalSw.Elapsed.TotalSeconds:F1}s");

        return new AuditReport
        {
            TargetPath = solutionPath,
            Timestamp = DateTime.UtcNow,
            TotalProjects = projectResults.Count,
            TotalDiagnostics = projectResults.Sum(r => r.TotalDiagnostics),
            Projects = projectResults,
        };
    }

    /// <summary>
    /// 审计 .slnx 解决方案：解析项目列表后逐个加载
    /// 使用单个 MSBuildWorkspace 避免重复初始化
    /// </summary>
    private async Task<AuditReport> AuditSlnxAsync(string slnxPath, bool skipTests, CancellationToken ct)
    {
        var totalSw = System.Diagnostics.Stopwatch.StartNew();
        var projectPaths = SlnxParser.ParseProjectPaths(slnxPath);
        Console.WriteLine($"  .slnx 包含 {projectPaths.Count} 个项目");

        // 过滤掉 Generator 项目和测试项目
        var filteredPaths = projectPaths
            .Where(p => !p.Contains("Generator", StringComparison.Ordinal))
            .Where(p => !skipTests || !IsTestProjectPath(p))
            .ToList();

        // 创建单个 workspace，所有项目共享
        var msbuildProps = CreateMsBuildProps();
        using var workspace = MSBuildWorkspace.Create(msbuildProps);
        workspace.SkipUnrecognizedProjects = true;
        workspace.LoadMetadataForReferencedProjects = false;

        // 阶段1：加载所有项目到 workspace（串行，MSBuildWorkspace 非线程安全）
        // MSBuildWorkspace 会自动加载依赖项目，需跳过已加载的
        var projects = new List<Project>();
        var loadSw = System.Diagnostics.Stopwatch.StartNew();
        var loadedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 先收集当前 workspace 已有的项目（理论上为空，但防御性编程）
        foreach (var p in workspace.CurrentSolution.Projects)
        {
            if (p.FilePath is not null)
                loadedPaths.Add(p.FilePath);
        }

        foreach (var projectPath in filteredPaths)
        {
            if (ct.IsCancellationRequested) break;

            // 跳过已加载的项目（可能作为依赖被自动加载）
            var fullPath = Path.GetFullPath(projectPath);
            if (loadedPaths.Contains(fullPath))
            {
                // 从 workspace 中获取已加载的项目
                var existing = workspace.CurrentSolution.Projects
                    .FirstOrDefault(p => p.FilePath is not null &&
                        string.Equals(Path.GetFullPath(p.FilePath), fullPath, StringComparison.OrdinalIgnoreCase));
                if (existing is not null)
                    projects.Add(existing);
                continue;
            }

            try
            {
                var project = await workspace.OpenProjectAsync(projectPath, cancellationToken: ct);
                projects.Add(project);

                // 记录此次加载引入的所有新项目（依赖项）
                foreach (var p in workspace.CurrentSolution.Projects)
                {
                    if (p.FilePath is not null)
                        loadedPaths.Add(Path.GetFullPath(p.FilePath));
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  加载项目失败: {Path.GetFileName(projectPath)} - {ex.Message}");
            }
        }
        loadSw.Stop();
        Console.WriteLine($"  项目加载完成 ({projects.Count}/{filteredPaths.Count})，耗时: {loadSw.Elapsed.TotalSeconds:F1}s");

        // 阶段2：并行编译+分析
        var projectResults = await ProcessProjectsParallelAsync(projects, ct);

        // 阶段3：DI 循环依赖检测（跨项目）
        var cycles = await DetectDiCyclesAsync(projects, ct);
        if (cycles.Count > 0)
        {
            Console.WriteLine($"  发现 {cycles.Count} 个 DI 循环依赖:");
            foreach (var cycle in cycles)
            {
                Console.WriteLine($"    循环: {string.Join(" → ", cycle.Path)}");
            }
        }

        // 将循环结果作为虚拟 "DI Cycles" 项目结果前置
        var cycleResults = BuildCycleAuditResults(cycles);
        projectResults = [.. cycleResults, .. projectResults];

        // 阶段4：胖构造函数检测（参数 > 阈值，建议中间件模式重构）
        var fatCtors = await DetectFatConstructorsAsync(projects, ct);
        if (fatCtors.Count > 0)
        {
            Console.WriteLine($"  发现 {fatCtors.Count} 个胖构造函数（参数 > {ConstructorParamCounter.DefaultThreshold}）:");
            foreach (var fc in fatCtors.OrderByDescending(c => c.ParameterCount).Take(10))
            {
                Console.WriteLine($"    [{fc.ParameterCount}] {fc.ClassName} - {fc.ConstructorSignature}");
            }
        }

        var fatCtorResults = BuildFatCtorAuditResults(fatCtors);
        projectResults = [.. fatCtorResults, .. projectResults];

        totalSw.Stop();
        Console.WriteLine($"审计完成，耗时: {totalSw.Elapsed.TotalSeconds:F1}s");

        return new AuditReport
        {
            TargetPath = slnxPath,
            Timestamp = DateTime.UtcNow,
            TotalProjects = projectResults.Count,
            TotalDiagnostics = projectResults.Sum(r => r.TotalDiagnostics),
            Projects = projectResults,
        };
    }

    /// <summary>
    /// 审计单个项目
    /// </summary>
    public async Task<AuditReport> AuditProjectAsync(string projectPath, CancellationToken ct = default)
    {
        var msbuildProps = CreateMsBuildProps();

        using var workspace = MSBuildWorkspace.Create(msbuildProps);
        workspace.SkipUnrecognizedProjects = true;

        var project = await workspace.OpenProjectAsync(projectPath, cancellationToken: ct);
        var result = await AuditProjectCoreAsync(project, ct);

        return new AuditReport
        {
            TargetPath = projectPath,
            Timestamp = DateTime.UtcNow,
            TotalProjects = 1,
            TotalDiagnostics = result.TotalDiagnostics,
            Projects = [result],
        };
    }

    /// <summary>
    /// 构造函数参数审计：扫描解决方案中所有胖构造函数（参数 > 阈值）
    /// 返回 ConstructorParamReport，用于独立 ctor-audit 子命令
    /// </summary>
    public async Task<ConstructorParamReport> AuditConstructorsAsync(
        string solutionPath, int threshold = ConstructorParamCounter.DefaultThreshold,
        bool skipTests = false, CancellationToken ct = default)
    {
        var totalSw = System.Diagnostics.Stopwatch.StartNew();
        Console.WriteLine($"正在加载解决方案（构造函数审计）: {solutionPath}");

        var ext = Path.GetExtension(solutionPath).ToLowerInvariant();
        List<Project> projects;

        var msbuildProps = CreateMsBuildProps();
        using var workspace = MSBuildWorkspace.Create(msbuildProps);
        workspace.SkipUnrecognizedProjects = true;
        workspace.LoadMetadataForReferencedProjects = false;

        if (ext == ".slnx" || ext == ".sln")
        {
            var projectPaths = SlnxParser.ParseProjectPaths(solutionPath);
            var filteredPaths = projectPaths
                .Where(p => !p.Contains("Generator", StringComparison.Ordinal))
                .Where(p => !skipTests || !IsTestProjectPath(p))
                .ToList();

            Console.WriteLine($"  包含 {filteredPaths.Count} 个项目");

            projects = [];
            var loadedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in workspace.CurrentSolution.Projects)
            {
                if (p.FilePath is not null)
                    loadedPaths.Add(p.FilePath);
            }

            foreach (var projectPath in filteredPaths)
            {
                if (ct.IsCancellationRequested) break;

                var fullPath = Path.GetFullPath(projectPath);
                if (loadedPaths.Contains(fullPath))
                {
                    var existing = workspace.CurrentSolution.Projects
                        .FirstOrDefault(p => p.FilePath is not null &&
                            string.Equals(Path.GetFullPath(p.FilePath), fullPath, StringComparison.OrdinalIgnoreCase));
                    if (existing is not null)
                        projects.Add(existing);
                    continue;
                }

                try
                {
                    var project = await workspace.OpenProjectAsync(projectPath, cancellationToken: ct);
                    projects.Add(project);
                    foreach (var p in workspace.CurrentSolution.Projects)
                    {
                        if (p.FilePath is not null)
                            loadedPaths.Add(Path.GetFullPath(p.FilePath));
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"  加载项目失败: {Path.GetFileName(projectPath)} - {ex.Message}");
                }
            }
        }
        else if (ext == ".csproj")
        {
            var project = await workspace.OpenProjectAsync(solutionPath, cancellationToken: ct);
            projects = [project];
        }
        else
        {
            throw new ArgumentException($"不支持的文件类型: {ext}。请提供 .csproj 或 .slnx 文件。");
        }

        Console.WriteLine($"  项目加载完成 ({projects.Count} 个)");

        // 并行提取胖构造函数
        var semaphore = new SemaphoreSlim(MaxConcurrency);
        var completed = 0;

        var tasks = projects.Select(async project =>
        {
            await semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                using var projectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                projectCts.CancelAfter(TimeSpan.FromSeconds(120));

                var compilation = await project.GetCompilationAsync(projectCts.Token).ConfigureAwait(false);
                if (compilation is null)
                {
                    Console.Error.WriteLine($"  编译失败: {project.Name}");
                    return Enumerable.Empty<ConstructorParamInfo>();
                }

                var fatCtors = ConstructorParamCounter.Extract(compilation, threshold);
                var count = Interlocked.Increment(ref completed);
                Console.WriteLine($"  [{count}/{projects.Count}] {project.Name} - {fatCtors.Count} 个胖构造函数");

                return fatCtors.AsEnumerable();
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                var count = Interlocked.Increment(ref completed);
                Console.Error.WriteLine($"  [{count}/{projects.Count}] 项目超时: {project.Name}");
                return Enumerable.Empty<ConstructorParamInfo>();
            }
            catch (Exception ex)
            {
                var count = Interlocked.Increment(ref completed);
                Console.Error.WriteLine($"  [{count}/{projects.Count}] 项目失败: {project.Name} - {ex.Message}");
                return Enumerable.Empty<ConstructorParamInfo>();
            }
            finally
            {
                semaphore.Release();
            }
        });

        var extractResults = await Task.WhenAll(tasks).ConfigureAwait(false);
        semaphore.Dispose();

        var allFatCtors = extractResults.SelectMany(r => r).ToList();

        totalSw.Stop();
        Console.WriteLine($"构造函数审计完成，耗时: {totalSw.Elapsed.TotalSeconds:F1}s");

        return new ConstructorParamReport
        {
            TargetPath = solutionPath,
            Timestamp = DateTime.UtcNow,
            Threshold = threshold,
            TotalFatCtors = allFatCtors.Count,
            Constructors = allFatCtors.OrderByDescending(c => c.ParameterCount).ToList(),
        };
    }

    /// <summary>
    /// 并行处理项目列表：编译 + 分析
    /// </summary>
    private async Task<List<ProjectAuditResult>> ProcessProjectsParallelAsync(
        IReadOnlyList<Project> projects, CancellationToken ct)
    {
        var results = new ProjectAuditResult[projects.Count];
        var completed = 0;
        var semaphore = new SemaphoreSlim(MaxConcurrency);

        var tasks = projects.Select(async (project, index) =>
        {
            await semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                using var projectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                projectCts.CancelAfter(TimeSpan.FromSeconds(120));

                var sw = System.Diagnostics.Stopwatch.StartNew();
                var result = await AuditProjectCoreAsync(project, projectCts.Token);
                sw.Stop();

                var count = Interlocked.Increment(ref completed);
                Console.WriteLine($"  [{count}/{projects.Count}] {project.Name} - {result.TotalDiagnostics} 条诊断 ({sw.Elapsed.TotalSeconds:F1}s)");

                results[index] = result;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                var count = Interlocked.Increment(ref completed);
                Console.Error.WriteLine($"  [{count}/{projects.Count}] 项目超时: {project.Name}");
                results[index] = new ProjectAuditResult
                {
                    ProjectName = project.Name,
                    ProjectPath = project.FilePath ?? string.Empty,
                };
            }
            catch (Exception ex)
            {
                var count = Interlocked.Increment(ref completed);
                Console.Error.WriteLine($"  [{count}/{projects.Count}] 项目失败: {project.Name} - {ex.Message}");
                results[index] = new ProjectAuditResult
                {
                    ProjectName = project.Name,
                    ProjectPath = project.FilePath ?? string.Empty,
                };
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
        semaphore.Dispose();
        return results.Where(r => r is not null).ToList()!;
    }

    /// <summary>
    /// 审计单个 Roslyn Project 对象（核心逻辑）
    /// </summary>
    private async Task<ProjectAuditResult> AuditProjectCoreAsync(Project project, CancellationToken ct)
    {
        // 获取编译结果
        var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
        if (compilation is null)
        {
            Console.Error.WriteLine($"  编译失败: {project.Name}");
            return new ProjectAuditResult
            {
                ProjectName = project.Name,
                ProjectPath = project.FilePath ?? string.Empty,
            };
        }

        // 调试：输出语法树数量
        Console.WriteLine($"  [{project.Name}] 语法树数量: {compilation.SyntaxTrees.Count()}");

        // 清除 NoWarn 对 JCC 规则的抑制，确保审计能检测到所有违规
        var specificOptions = compilation.Options.SpecificDiagnosticOptions;
        var modifiedOptions = new Dictionary<string, ReportDiagnostic>(specificOptions);
        foreach (var kvp in specificOptions)
        {
            if (kvp.Key.StartsWith("JCC", StringComparison.Ordinal) &&
                kvp.Value == ReportDiagnostic.Suppress)
            {
                modifiedOptions[kvp.Key] = ReportDiagnostic.Warn;
            }
        }
        var newOptions = compilation.Options.WithSpecificDiagnosticOptions(
            modifiedOptions.ToImmutableDictionary());
        compilation = compilation.WithOptions(newOptions);

        // 添加分析器并获取诊断
        var compilationWithAnalyzers = compilation.WithAnalyzers(_analyzers.ToImmutableArray());
        var allDiagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync(ct).ConfigureAwait(false);
        var diagnostics = allDiagnostics
            .Where(d => d.Id.StartsWith("JCC", StringComparison.Ordinal))
            .ToList();

        var auditDiagnostics = diagnostics.Select(d =>
        {
            var lineSpan = d.Location.GetLineSpan();
            return new AuditDiagnostic
            {
                RuleId = d.Id,
                Severity = d.Severity.ToString(),
                Message = d.GetMessage(),
                FilePath = lineSpan.Path,
                Line = lineSpan.StartLinePosition.Line + 1,
                Column = lineSpan.StartLinePosition.Character + 1,
                Category = d.Descriptor.Category,
            };
        }).ToList();

        return new ProjectAuditResult
        {
            ProjectName = project.Name,
            ProjectPath = project.FilePath ?? string.Empty,
            TotalDiagnostics = auditDiagnostics.Count,
            WarningCount = auditDiagnostics.Count(d => d.Severity == "Warning"),
            ErrorCount = auditDiagnostics.Count(d => d.Severity == "Error"),
            InfoCount = auditDiagnostics.Count(d => d.Severity == "Info" || d.Severity == "Hidden"),
            Diagnostics = auditDiagnostics,
        };
    }

    /// <summary>
    /// 创建 MSBuild 属性字典：跳过不必要的构建步骤
    /// </summary>
    private static Dictionary<string, string> CreateMsBuildProps()
    {
        return new Dictionary<string, string>
        {
            ["BuildProjectReferences"] = "false",
            ["SkipResolvePackageAssets"] = "true",
        };
    }

    /// <summary>
    /// 判断项目名称是否为测试项目
    /// </summary>
    private static bool IsTestProject(string projectName)
    {
        return projectName.Contains("Test", StringComparison.Ordinal) ||
               projectName.Contains("Benchmark", StringComparison.Ordinal) ||
               projectName.Contains("MockServer", StringComparison.Ordinal) ||
               projectName.Contains("E2E", StringComparison.Ordinal);
    }

    /// <summary>
    /// 判断项目路径是否为测试项目
    /// </summary>
    private static bool IsTestProjectPath(string projectPath)
    {
        return projectPath.Contains("\\tests\\", StringComparison.Ordinal) ||
               projectPath.Contains("/tests/", StringComparison.Ordinal) ||
               projectPath.Contains("MockServer", StringComparison.Ordinal);
    }

    /// <summary>
    /// 检测所有项目的 DI 循环依赖
    /// </summary>
    private async Task<List<DiCycleInfo>> DetectDiCyclesAsync(
        IReadOnlyList<Project> projects, CancellationToken ct)
    {
        var allRegistrations = new List<ServiceRegistration>();
        var allDependencies = new List<ConstructorDependency>();

        // 从每个项目中提取 DI 注册和构造函数依赖
        var extractTasks = projects.Select(async project =>
        {
            using var projectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            projectCts.CancelAfter(TimeSpan.FromSeconds(120));

            var compilation = await project.GetCompilationAsync(projectCts.Token).ConfigureAwait(false);
            if (compilation is null)
                return (Enumerable.Empty<ServiceRegistration>(), Enumerable.Empty<ConstructorDependency>());

            var (regs, deps) = DiRegistrationExtractor.Extract(compilation);
            return (regs, deps);
        });

        var extractResults = await Task.WhenAll(extractTasks).ConfigureAwait(false);
        foreach (var (regs, deps) in extractResults)
        {
            allRegistrations.AddRange(regs);
            allDependencies.AddRange(deps);
        }

        // 构建服务到实现的映射
        var serviceToImpl = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var reg in allRegistrations)
        {
            if (reg.ServiceType != reg.ImplementationType)
                serviceToImpl[reg.ServiceType] = reg.ImplementationType;
        }

        // 检测循环
        var cycles = DiCycleDetector.DetectCycles(allRegistrations, allDependencies, serviceToImpl);
        return cycles;
    }

    /// <summary>
    /// 将 DI 循环结果转换为 ProjectAuditResult
    /// </summary>
    private static ProjectAuditResult[] BuildCycleAuditResults(List<DiCycleInfo> cycles)
    {
        if (cycles.Count == 0)
            return [];

        var diagnostics = new List<AuditDiagnostic>(cycles.Count);
        foreach (var cycle in cycles)
        {
            var cyclePath = string.Join(" → ", cycle.Path);
            var message = $"DI 循环依赖: {cyclePath}。服务在构造时相互依赖，会导致 StackOverflowException。";

            // 为循环的每条边创建诊断
            foreach (var edge in cycle.Edges)
            {
                diagnostics.Add(new AuditDiagnostic
                {
                    RuleId = "JCC9001",
                    Severity = "Error",
                    Message = message,
                    FilePath = edge.File ?? string.Empty,
                    Line = edge.Line ?? 0,
                    Column = 1,
                    Category = "DiCycle",
                });
            }
        }

        return [new ProjectAuditResult
        {
            ProjectName = "DI Cycles",
            ProjectPath = string.Empty,
            TotalDiagnostics = diagnostics.Count,
            ErrorCount = diagnostics.Count,
            WarningCount = 0,
            InfoCount = 0,
            Diagnostics = diagnostics,
        }];
    }

    /// <summary>
    /// 检测所有项目的胖构造函数（参数数量超过阈值）
    /// </summary>
    private async Task<List<ConstructorParamInfo>> DetectFatConstructorsAsync(
        IReadOnlyList<Project> projects, CancellationToken ct)
    {
        var allFatCtors = new List<ConstructorParamInfo>();

        var extractTasks = projects.Select(async project =>
        {
            using var projectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            projectCts.CancelAfter(TimeSpan.FromSeconds(120));

            var compilation = await project.GetCompilationAsync(projectCts.Token).ConfigureAwait(false);
            if (compilation is null)
                return Enumerable.Empty<ConstructorParamInfo>();

            return ConstructorParamCounter.Extract(compilation);
        });

        var extractResults = await Task.WhenAll(extractTasks).ConfigureAwait(false);
        foreach (var ctors in extractResults)
        {
            allFatCtors.AddRange(ctors);
        }

        return allFatCtors;
    }

    /// <summary>
    /// 将胖构造函数结果转换为 ProjectAuditResult
    /// </summary>
    private static ProjectAuditResult[] BuildFatCtorAuditResults(List<ConstructorParamInfo> fatCtors)
    {
        if (fatCtors.Count == 0)
            return [];

        var diagnostics = new List<AuditDiagnostic>(fatCtors.Count);
        foreach (var ctor in fatCtors)
        {
            var paramList = string.Join(", ", ctor.ParameterTypes);
            var message = $"胖构造函数: {ctor.ClassName} 有 {ctor.ParameterCount} 个参数（{paramList}）。" +
                         $"建议重构为中间件模式，将相关参数聚合为中间件上下文，减少构造函数注入。";

            diagnostics.Add(new AuditDiagnostic
            {
                RuleId = "JCC9003",
                Severity = ctor.ParameterCount >= 12 ? "Error" : "Warning",
                Message = message,
                FilePath = ctor.FilePath,
                Line = ctor.LineNumber,
                Column = 1,
                Category = "FatConstructor",
            });
        }

        var errorCount = diagnostics.Count(d => d.Severity == "Error");
        var warningCount = diagnostics.Count(d => d.Severity == "Warning");

        return [new ProjectAuditResult
        {
            ProjectName = "Fat Constructors",
            ProjectPath = string.Empty,
            TotalDiagnostics = diagnostics.Count,
            ErrorCount = errorCount,
            WarningCount = warningCount,
            InfoCount = 0,
            Diagnostics = diagnostics,
        }];
    }
}
