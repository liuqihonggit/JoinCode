namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC10001/JCC10002: 项目结构检测(文件过多/文件文件夹混放)。
/// 多描述符规则 — 共享 AnalyzeProjectStructure 逻辑。
/// 用 RegisterCompilationEndAction 替代 RegisterCompilationAction(IAnalyzerRule.Register 接收 CompilationStartAnalysisContext)。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "CodeOrganization",
    Id = "JCC10001",
    Title = "项目结构: 文件夹内直接暴露文件超过20个应拆分",
    Description = "文件夹 '{0}' 内直接暴露 {1} 个文件，超过20个上限。应按职责拆分为多层子文件夹，每个文件夹内直接暴露文件少于20个。",
    Category = "ProjectStructure",
    Severity = DiagnosticSeverity.Warning,
    IsEnabledByDefault = true,
    HelpLinkUri = "文件夹内直接暴露文件超过20个会降低可导航性和可维护性. 正确做法: 1) 按职责/功能/层级拆分子文件夹; 2) 每个子文件夹内文件数 < 20; 3) 可以多层嵌套. 排除: bin/obj/.x/ 目录和生成的代码.",
    IsCompilationEnd = true)]
[AnalyzerRule(
    AnalyzerId = "CodeOrganization",
    Id = "JCC10002",
    Title = "项目结构: 文件夹内文件和文件夹不应同时存在",
    Description = "文件夹 '{0}' 内同时存在 {1} 个文件和 {2} 个子文件夹，违反纯文件夹或纯文件原则。应将文件移入子文件夹或将子文件夹提升，确保每个文件夹内要么纯文件夹要么纯文件。",
    Category = "ProjectStructure",
    Severity = DiagnosticSeverity.Warning,
    IsEnabledByDefault = true,
    HelpLinkUri = "文件夹内文件和文件夹混放降低代码组织一致性. 正确做法: 1) 纯文件夹模式: 所有直接子项都是文件夹; 2) 纯文件模式: 所有直接子项都是文件; 3) 优先采用纯文件夹模式，文件放入对应子文件夹. 排除: bin/obj/.x/ 目录和生成的代码.",
    IsCompilationEnd = true)]
public sealed class ProjectStructureRule : IAnalyzerRule {
    private static readonly IReadOnlyDictionary<string, DiagnosticDescriptor> Map = RuleDescriptorFactory.CreateAll<ProjectStructureRule>();
    public IReadOnlyList<DiagnosticDescriptor> Descriptors { get; } = Map.Values.ToList();

    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", ".x", ".vs", ".idea", ".git", "node_modules",
    };

    public void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(_ => { }, SyntaxKind.CompilationUnit);
        context.RegisterCompilationEndAction(AnalyzeProjectStructure);
    }

    private static void AnalyzeProjectStructure(CompilationAnalysisContext ctx) {
        if (ctx.CancellationToken.IsCancellationRequested) return;

        var compilation = ctx.Compilation;
        var syntaxTrees = compilation.SyntaxTrees;

        var directoryFiles = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var directorySubdirs = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var tree in syntaxTrees) {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            var filePath = tree.FilePath;
            if (string.IsNullOrEmpty(filePath)) continue;

            if (IsInExcludedPath(filePath)) continue;

            var lastSlash = filePath.LastIndexOfAny(new[] { '\\', '/' });
            if (lastSlash < 0) continue;

            var dir = filePath.Substring(0, lastSlash);
            var fileName = filePath.Substring(lastSlash + 1);

            if (!directoryFiles.ContainsKey(dir))
                directoryFiles[dir] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            directoryFiles[dir].Add(fileName);

            var parentSlash = dir.LastIndexOfAny(new[] { '\\', '/' });
            if (parentSlash >= 0) {
                var parentDir = dir.Substring(0, parentSlash);
                var subdirName = dir.Substring(parentSlash + 1);
                if (!directorySubdirs.ContainsKey(parentDir))
                    directorySubdirs[parentDir] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                directorySubdirs[parentDir].Add(subdirName);
            }
        }

        var allDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in directoryFiles.Keys) allDirs.Add(d);
        foreach (var d in directorySubdirs.Keys) allDirs.Add(d);

        foreach (var dir in allDirs) {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            var fileCount = directoryFiles.TryGetValue(dir, out var files) ? files.Count : 0;
            if (fileCount > 20) {
                var lastSlash = dir.LastIndexOfAny(new[] { '\\', '/' });
                var dirName = lastSlash >= 0 ? dir.Substring(lastSlash + 1) : dir;
                var location = compilation.SyntaxTrees
                    .FirstOrDefault(t => t.FilePath.StartsWith(dir, StringComparison.OrdinalIgnoreCase))
                    ?.GetRoot().GetLocation();
                if (location is not null)
                    ctx.ReportDiagnostic(Diagnostic.Create(Map["JCC10001"], location, dirName, fileCount));
            }
        }

        foreach (var dir in allDirs) {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            var fileCount = directoryFiles.TryGetValue(dir, out var files) ? files.Count : 0;
            var subdirCount = directorySubdirs.TryGetValue(dir, out var subdirs) ? subdirs.Count : 0;

            if (fileCount > 0 && subdirCount > 0) {
                if (files!.All(f => f.StartsWith("GlobalUsings", StringComparison.OrdinalIgnoreCase) || f.Equals("AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase)))
                    continue;
                var lastSlash = dir.LastIndexOfAny(new[] { '\\', '/' });
                var dirName = lastSlash >= 0 ? dir.Substring(lastSlash + 1) : dir;
                var location = compilation.SyntaxTrees
                    .FirstOrDefault(t => t.FilePath.StartsWith(dir, StringComparison.OrdinalIgnoreCase))
                    ?.GetRoot().GetLocation();
                if (location is not null)
                    ctx.ReportDiagnostic(Diagnostic.Create(Map["JCC10002"], location, dirName, fileCount, subdirCount));
            }
        }
    }

    private static bool IsInExcludedPath(string filePath) {
        var parts = filePath.Split(new[] { '\\', '/' });
        foreach (var part in parts) {
            if (ExcludedDirectories.Contains(part))
                return true;
        }
        return false;
    }
}
