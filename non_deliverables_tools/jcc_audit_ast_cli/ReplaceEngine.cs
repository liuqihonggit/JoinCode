namespace JccAuditCli;

/// <summary>
/// AST 批量替换引擎：加载 CodeFixProvider 并应用修复到磁盘文件
/// </summary>
public sealed class ReplaceEngine
{
    private readonly List<DiagnosticAnalyzer> _analyzers;
    private readonly List<CodeFixProvider> _codeFixProviders;

    public ReplaceEngine(List<DiagnosticAnalyzer> analyzers, List<CodeFixProvider> codeFixProviders)
    {
        _analyzers = analyzers;
        _codeFixProviders = codeFixProviders;
    }

    /// <summary>
    /// 对解决方案应用指定规则的 CodeFix
    /// 支持 .sln（MSBuildWorkspace 原生）和 .slnx（手动解析后逐个加载）
    /// </summary>
    public async Task<ReplaceResult> ReplaceSolutionAsync(
        string solutionPath, string ruleId, bool fixAll, bool dryRun, CancellationToken ct)
    {
        Console.WriteLine($"正在加载解决方案: {solutionPath}");

        var ext = Path.GetExtension(solutionPath).ToLowerInvariant();

        // .slnx 格式：手动解析项目列表，逐个加载
        if (ext == ".slnx")
        {
            return await ReplaceSlnxAsync(solutionPath, ruleId, fixAll, dryRun, ct);
        }

        // .sln 格式：使用 MSBuildWorkspace 原生加载
        var msbuildProps = new Dictionary<string, string>
        {
            ["BuildProjectReferences"] = "false",
            ["SkipResolvePackageAssets"] = "true",
        };

        using var workspace = MSBuildWorkspace.Create(msbuildProps);
        workspace.SkipUnrecognizedProjects = true;
        workspace.LoadMetadataForReferencedProjects = false;

        var solution = await workspace.OpenSolutionAsync(solutionPath, cancellationToken: ct);
        var result = new ReplaceResult();

        foreach (var project in solution.Projects)
        {
            if (project.Name.Contains("Generator", StringComparison.Ordinal))
                continue;

            var (modifiedSolution, projectResult) = await ReplaceProjectAsync(
                project, ruleId, fixAll, dryRun, ct);

            result.ProjectResults.Add(projectResult);
            result.ModifiedSolution = modifiedSolution;
        }

        // 应用更改到磁盘
        if (!dryRun && result.HasChanges)
        {
            var success = workspace.TryApplyChanges(result.ModifiedSolution ?? throw new InvalidOperationException("ModifiedSolution is null despite HasChanges being true."));
            if (!success)
            {
                Console.Error.WriteLine("工作区应用更改失败。");
                result.ApplySuccess = false;
            }
            else
            {
                result.ApplySuccess = true;
                Console.WriteLine($"已将 {result.TotalFixesApplied} 处修复写入磁盘。");
            }
        }

        return result;
    }

    /// <summary>
    /// 对 .slnx 解决方案应用 CodeFix：解析项目列表后逐个加载
    /// </summary>
    private async Task<ReplaceResult> ReplaceSlnxAsync(
        string slnxPath, string ruleId, bool fixAll, bool dryRun, CancellationToken ct)
    {
        var projectPaths = SlnxParser.ParseProjectPaths(slnxPath);
        Console.WriteLine($"  .slnx 包含 {projectPaths.Count} 个项目");

        var result = new ReplaceResult();

        foreach (var projectPath in projectPaths)
        {
            if (ct.IsCancellationRequested) break;

            // 跳过 Generator 项目
            if (projectPath.Contains("Generator", StringComparison.Ordinal))
            {
                Console.WriteLine($"  跳过 Generator 项目: {Path.GetFileName(projectPath)}");
                continue;
            }

            try
            {
                var projectResult = await ReplaceProjectAsync(projectPath, ruleId, fixAll, dryRun, ct);
                result.ProjectResults.AddRange(projectResult.ProjectResults);
                if (projectResult.ApplySuccess == true)
                    result.ApplySuccess = true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  加载项目失败: {projectPath} - {ex.Message}");
            }
        }

        return result;
    }

    /// <summary>
    /// 对单个项目应用指定规则的 CodeFix
    /// </summary>
    public async Task<ReplaceResult> ReplaceProjectAsync(
        string projectPath, string ruleId, bool fixAll, bool dryRun, CancellationToken ct)
    {
        Console.WriteLine($"正在加载项目: {projectPath}");

        var msbuildProps = new Dictionary<string, string>
        {
            ["BuildProjectReferences"] = "false",
        };

        using var workspace = MSBuildWorkspace.Create(msbuildProps);
        workspace.SkipUnrecognizedProjects = true;

        var project = await workspace.OpenProjectAsync(projectPath, cancellationToken: ct);
        var (modifiedSolution, projectResult) = await ReplaceProjectAsync(
            project, ruleId, fixAll, dryRun, ct);

        var result = new ReplaceResult
        {
            ModifiedSolution = modifiedSolution,
        };
        result.ProjectResults.Add(projectResult);

        if (!dryRun && result.HasChanges)
        {
            var success = workspace.TryApplyChanges(result.ModifiedSolution ?? throw new InvalidOperationException("ModifiedSolution is null despite HasChanges being true."));
            result.ApplySuccess = success;
        }

        return result;
    }

    /// <summary>
    /// 对单个 Roslyn Project 应用 CodeFix
    /// </summary>
    private async Task<(Solution? ModifiedSolution, ProjectReplaceResult Result)> ReplaceProjectAsync(
        Project project, string ruleId, bool fixAll, bool dryRun, CancellationToken ct)
    {
        var result = new ProjectReplaceResult
        {
            ProjectName = project.Name,
            ProjectPath = project.FilePath ?? string.Empty,
        };

        Console.WriteLine($"  编译项目: {project.Name}");

        var compilation = await project.GetCompilationAsync(ct);
        if (compilation is null)
        {
            Console.Error.WriteLine($"  编译失败: {project.Name}");
            return (null, result);
        }

        // 获取诊断
        var compilationWithAnalyzers = compilation.WithAnalyzers(_analyzers.ToImmutableArray());
        var allDiagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync(ct);
        var targetDiagnostics = allDiagnostics
            .Where(d => d.Id == ruleId)
            .ToList();

        if (targetDiagnostics.Count == 0)
        {
            Console.WriteLine($"  项目 {project.Name}: 无 {ruleId} 诊断，跳过。");
            return (project.Solution, result);
        }

        Console.WriteLine($"  项目 {project.Name}: 发现 {targetDiagnostics.Count} 条 {ruleId} 诊断");

        // 查找匹配的 CodeFixProvider
        var matchingProviders = _codeFixProviders
            .Where(p => p.FixableDiagnosticIds.Contains(ruleId))
            .ToList();

        if (matchingProviders.Count == 0)
        {
            Console.WriteLine($"  项目 {project.Name}: 无匹配的 CodeFixProvider (规则: {ruleId})");
            Console.WriteLine($"  可用的 CodeFixProvider 规则: {string.Join(", ", _codeFixProviders.SelectMany(p => p.FixableDiagnosticIds).Distinct())}");
            result.DiagnosticsFound = targetDiagnostics.Count;
            return (project.Solution, result);
        }

        // 应用 CodeFix（逐个修复模式，循环直到无更多诊断）
        var currentSolution = project.Solution;
        var totalFixes = 0;
        var maxIterations = fixAll ? targetDiagnostics.Count * 2 : 1;

        for (var iteration = 0; iteration < maxIterations; iteration++)
        {
            // 重新编译获取最新诊断
            var currentProject = currentSolution.GetProject(project.Id);
            if (currentProject is null) break;

            var currentCompilation = await currentProject.GetCompilationAsync(ct);
            if (currentCompilation is null) break;

            var currentCompilationWithAnalyzers = currentCompilation.WithAnalyzers(_analyzers.ToImmutableArray());
            var currentDiagnostics = await currentCompilationWithAnalyzers.GetAnalyzerDiagnosticsAsync(ct);
            var remainingDiagnostics = currentDiagnostics.Where(d => d.Id == ruleId).ToList();

            if (remainingDiagnostics.Count == 0) break;

            // 只修复第一个诊断
            var diagnostic = remainingDiagnostics[0];
            var wasFixed = false;

            foreach (var provider in matchingProviders)
            {
                var fixedSolution = await TryApplyCodeFix(
                    currentProject, provider, diagnostic, dryRun, ct);

                if (fixedSolution is not null)
                {
                    currentSolution = fixedSolution;
                    totalFixes++;
                    wasFixed = true;

                    if (dryRun)
                    {
                        // DryRun 模式下记录将修改的文件
                        var changes = currentSolution.GetChanges(project.Solution);
                        foreach (var projectChange in changes.GetProjectChanges())
                        {
                            foreach (var docId in projectChange.GetChangedDocuments())
                            {
                                var doc = currentSolution.GetDocument(docId);
                                if (doc?.FilePath is not null)
                                {
                                    result.ModifiedFiles.Add(doc.FilePath);
                                    Console.WriteLine($"    [DryRun] 将修改: {doc.FilePath}");
                                }
                            }
                        }
                    }
                    else
                    {
                        var lineSpan = diagnostic.Location.GetLineSpan();
                        Console.WriteLine($"    已修复: {lineSpan.Path}:{lineSpan.StartLinePosition.Line + 1}");
                    }

                    break;
                }
            }

            if (!wasFixed)
            {
                Console.WriteLine($"    无法修复诊断: {diagnostic.Location.GetLineSpan().Path}:{diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1}");
                break;
            }

            // 非全部修复模式，只修复一个就退出
            if (!fixAll) break;
        }

        result.DiagnosticsFound = targetDiagnostics.Count;
        result.FixesApplied = totalFixes;

        return (currentSolution, result);
    }

    /// <summary>
    /// 尝试对单个诊断应用 CodeFix
    /// </summary>
    private static async Task<Solution?> TryApplyCodeFix(
        Project project, CodeFixProvider provider, Diagnostic diagnostic, bool dryRun, CancellationToken ct)
    {
        // 找到诊断所在的文档
        var tree = diagnostic.Location.SourceTree;
        if (tree is null) return null;

        var document = project.Documents.FirstOrDefault(d => d.FilePath == tree.FilePath);
        if (document is null) return null;

        // 收集 CodeAction
        var codeActions = new List<CodeAction>();

        var codeFixContext = new CodeFixContext(
            document,
            diagnostic,
            (action, _) => codeActions.Add(action),
            ct);

        try
        {
            await provider.RegisterCodeFixesAsync(codeFixContext);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"    RegisterCodeFixes 失败: {ex.Message}");
            return null;
        }

        if (codeActions.Count == 0) return null;

        // 应用第一个 CodeAction
        var codeAction = codeActions[0];
        var operations = await codeAction.GetOperationsAsync(ct);
        var applyOperation = operations.OfType<ApplyChangesOperation>().FirstOrDefault();

        if (applyOperation is null) return null;

        if (dryRun)
        {
            // DryRun: 返回修改后的 Solution 但不实际应用
            return applyOperation.ChangedSolution;
        }

        return applyOperation.ChangedSolution;
    }
}

/// <summary>
/// 替换结果
/// </summary>
public sealed class ReplaceResult
{
    public List<ProjectReplaceResult> ProjectResults { get; init; } = [];
    public Solution? ModifiedSolution { get; set; }
    public bool? ApplySuccess { get; set; }
    public bool HasChanges => ProjectResults.Any(r => r.FixesApplied > 0 || r.ModifiedFiles.Count > 0);
    public int TotalFixesApplied => ProjectResults.Sum(r => r.FixesApplied);
}

/// <summary>
/// 项目替换结果
/// </summary>
public sealed class ProjectReplaceResult
{
    public string ProjectName { get; init; } = string.Empty;
    public string ProjectPath { get; init; } = string.Empty;
    public int DiagnosticsFound { get; set; }
    public int FixesApplied { get; set; }
    public List<string> ModifiedFiles { get; init; } = [];
}
