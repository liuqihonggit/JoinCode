namespace JccAuditCli;

/// <summary>
/// 分析器驱动修复器 — 调用共享检测器（SyncMethodAsyncCallDetector）定位违规，应用修复。
/// 检测逻辑与分析器完全一致（共享同一份代码），修复逻辑在本类中实现。
/// 不再需要加载分析器 DLL，直接调用共享工程中的检测方法。
/// </summary>
public static class AnalyzerDrivenFixer {

    /// <summary>
    /// 修复解决方案中所有 JCC3016 违规。
    /// 检测：调用 SyncMethodAsyncCallDetector.IsViolation()（与分析器共享同一套逻辑）。
    /// 修复：invocation → invocation.GetAwaiter().GetResult()。
    /// </summary>
    /// <param name="solutionPath">解决方案路径（.slnx / .sln / .csproj）。</param>
    /// <param name="dryRun">仅预览，不实际写入。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>（修复文件数, 修复问题数, 跳过数）。</returns>
    public static async Task<(int fixedFiles, int fixedIssues, int skipped)> FixAsync(
        string solutionPath, bool dryRun, CancellationToken ct) {

        Console.WriteLine($"  规则: JCC3016（共享检测器）");
        Console.WriteLine($"  模式: {(dryRun ? "预览 (DryRun)" : "实际写入")}");

        // ── 阶段1：加载解决方案 ──
        var msbuildProps = new Dictionary<string, string> {
            ["BuildProjectReferences"] = "false",
            ["SkipResolvePackageAssets"] = "true",
        };

        using var workspace = MSBuildWorkspace.Create(msbuildProps);
        workspace.SkipUnrecognizedProjects = true;
        workspace.LoadMetadataForReferencedProjects = false;

        var projects = await LoadProjectsAsync(solutionPath, workspace, ct).ConfigureAwait(false);
        Console.WriteLine($"  已加载 {projects.Count} 个项目");

        // ── 阶段2：逐项目编译，用共享检测器检测违规 ──
        // 按 SyntaxTree 分组收集违规的 invocation 节点
        var violationsByTree = new Dictionary<SyntaxTree, List<InvocationExpressionSyntax>>();

        foreach (var project in projects) {
            if (ct.IsCancellationRequested) break;

            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation is null) continue;

            var projectViolations = 0;

            foreach (var tree in compilation.SyntaxTrees) {
                if (ct.IsCancellationRequested) break;
                if (ShouldSkipFile(tree.FilePath)) continue;

                var root = await tree.GetRootAsync(ct).ConfigureAwait(false);
                var semanticModel = compilation.GetSemanticModel(tree);

                // 用共享检测器检测违规 — 与分析器调用同一套逻辑
                var violations = root.DescendantNodes()
                    .OfType<ExpressionStatementSyntax>()
                    .Where(stmt => SyncMethodAsyncCallDetector.IsViolation(stmt, semanticModel, ct))
                    .Select(stmt => SyncMethodAsyncCallDetector.GetInvocation(stmt))
                    .Where(inv => inv is not null)
                    .Cast<InvocationExpressionSyntax>()
                    .ToList();

                if (violations.Count == 0) continue;

                projectViolations += violations.Count;
                // 同一 SyntaxTree 可能被多个项目引用，取第一次检测结果
                if (!violationsByTree.ContainsKey(tree))
                    violationsByTree[tree] = violations;
            }

            if (projectViolations > 0)
                Console.WriteLine($"  [{project.Name}] 发现 {projectViolations} 处 JCC3016 违规");
        }

        if (violationsByTree.Count == 0) {
            Console.WriteLine("  未发现违规，无需修复。");
            return (0, 0, 0);
        }

        // ── 阶段3：按 SyntaxTree 应用修复 ──
        var writtenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int fixedFiles = 0, fixedIssues = 0, skipped = 0;

        foreach (var (tree, invocations) in violationsByTree) {
            if (ct.IsCancellationRequested) break;

            var filePath = tree.FilePath;
            if (string.IsNullOrEmpty(filePath)) continue;
            if (writtenFiles.Contains(filePath)) continue;

            var root = await tree.GetRootAsync(ct).ConfigureAwait(false);

            // 一次性替换所有违规 invocation → invocation.GetAwaiter().GetResult()
            var newRoot = root.ReplaceNodes(invocations,
                (oldNode, _) => CreateGetAwaiterGetResult(oldNode));

            if (newRoot != root) {
                fixedFiles++;
                fixedIssues += invocations.Count;
                writtenFiles.Add(filePath);

                if (!dryRun) {
                    var newText = newRoot.ToFullString();
                    await File.WriteAllTextAsync(filePath, newText, ct).ConfigureAwait(false);
                }

                Console.WriteLine($"  {Path.GetFileName(filePath)}: {invocations.Count} 处修复");
            } else {
                skipped++;
            }
        }

        return (fixedFiles, fixedIssues, skipped);
    }

    /// <summary>
    /// 加载解决方案中的所有项目（支持 .slnx / .sln / .csproj）
    /// </summary>
    private static async Task<List<Project>> LoadProjectsAsync(
        string solutionPath, MSBuildWorkspace workspace, CancellationToken ct) {

        var ext = Path.GetExtension(solutionPath).ToLowerInvariant();

        if (ext == ".sln") {
            var solution = await workspace.OpenSolutionAsync(solutionPath, cancellationToken: ct).ConfigureAwait(false);
            return solution.Projects
                .Where(p => !p.Name.Contains("Generator", StringComparison.Ordinal))
                .ToList();
        }

        if (ext == ".slnx") {
            return await LoadSlnxProjectsAsync(solutionPath, workspace, ct).ConfigureAwait(false);
        }

        if (ext == ".csproj") {
            var project = await workspace.OpenProjectAsync(solutionPath, cancellationToken: ct).ConfigureAwait(false);
            return [project];
        }

        throw new ArgumentException($"不支持的文件类型: {ext}");
    }

    /// <summary>
    /// 加载 .slnx 解决方案中的项目
    /// </summary>
    private static async Task<List<Project>> LoadSlnxProjectsAsync(
        string slnxPath, MSBuildWorkspace workspace, CancellationToken ct) {
        var projectPaths = await SlnxParser.ParseProjectPaths(slnxPath).ConfigureAwait(false);
        var projects = new List<Project>();
        var loadedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in workspace.CurrentSolution.Projects) {
            if (p.FilePath is not null)
                loadedPaths.Add(Path.GetFullPath(p.FilePath));
        }

        foreach (var projectPath in projectPaths) {
            if (ct.IsCancellationRequested) break;
            await TryLoadProjectAsync(projectPath, workspace, projects, loadedPaths, ct).ConfigureAwait(false);
        }

        return projects;
    }

    /// <summary>
    /// 尝试加载单个项目（已加载则复用，否则新加载）
    /// </summary>
    private static async Task TryLoadProjectAsync(
        string projectPath, MSBuildWorkspace workspace,
        List<Project> projects, HashSet<string> loadedPaths, CancellationToken ct) {
        var fullPath = Path.GetFullPath(projectPath);
        if (loadedPaths.Contains(fullPath)) {
            AddExistingProject(workspace, projects, fullPath);
            return;
        }

        try {
            var project = await workspace.OpenProjectAsync(projectPath, cancellationToken: ct).ConfigureAwait(false);
            if (!project.Name.Contains("Generator", StringComparison.Ordinal))
                projects.Add(project);

            foreach (var p in workspace.CurrentSolution.Projects) {
                if (p.FilePath is not null)
                    loadedPaths.Add(Path.GetFullPath(p.FilePath));
            }
        } catch (Exception ex) {
            Console.Error.WriteLine($"  加载项目失败: {Path.GetFileName(projectPath)} - {ex.Message}");
        }
    }

    /// <summary>
    /// 从 workspace 中查找已加载的项目并添加到列表
    /// </summary>
    private static void AddExistingProject(MSBuildWorkspace workspace, List<Project> projects, string fullPath) {
        var existing = workspace.CurrentSolution.Projects
            .FirstOrDefault(p => p.FilePath is not null &&
                string.Equals(Path.GetFullPath(p.FilePath), fullPath, StringComparison.OrdinalIgnoreCase));
        if (existing is not null && !existing.Name.Contains("Generator", StringComparison.Ordinal))
            projects.Add(existing);
    }

    /// <summary>
    /// 创建 invocation.GetAwaiter().GetResult() 表达式。
    /// 保留原始 invocation 的 leading/trailing trivia。
    /// </summary>
    private static ExpressionSyntax CreateGetAwaiterGetResult(InvocationExpressionSyntax invocation) {
        var getAwaiterAccess = SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            invocation.WithoutTrailingTrivia(),
            SyntaxFactory.Token(SyntaxKind.DotToken),
            SyntaxFactory.IdentifierName("GetAwaiter"));

        var getAwaiterCall = SyntaxFactory.InvocationExpression(getAwaiterAccess);

        var getResultAccess = SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            getAwaiterCall,
            SyntaxFactory.Token(SyntaxKind.DotToken),
            SyntaxFactory.IdentifierName("GetResult"));

        var getResultCall = SyntaxFactory.InvocationExpression(getResultAccess);

        return getResultCall
            .WithLeadingTrivia(invocation.GetLeadingTrivia())
            .WithTrailingTrivia(invocation.GetTrailingTrivia());
    }

    /// <summary>
    /// 跳过生成代码和构建产物
    /// </summary>
    private static bool ShouldSkipFile(string? filePath) {
        if (string.IsNullOrEmpty(filePath)) return true;
        var normalized = filePath.Replace('\\', '/');
        if (normalized.Contains("/artifacts/")) return true;
        if (normalized.Contains("/obj/")) return true;
        if (normalized.Contains("/bin/")) return true;
        if (normalized.Contains("/bcl_bridge/")) return true;
        if (normalized.Contains("/aot_safety.generator/")) return true;
        if (normalized.Contains("/aot_safety.shared/")) return true;
        return false;
    }
}
