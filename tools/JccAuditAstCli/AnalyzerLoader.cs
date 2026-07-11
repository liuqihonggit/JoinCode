namespace JccAuditCli;

/// <summary>
/// 分析器加载器：从 DLL 文件加载 Roslyn DiagnosticAnalyzer
/// </summary>
public static class AnalyzerLoader
{
    /// <summary>
    /// 从指定目录加载所有分析器 DLL
    /// </summary>
    /// <param name="analyzerDirectory">DLL 搜索目录</param>
    /// <param name="filter">可选规则过滤（如 JCC3007），只加载包含该规则的分析器</param>
    public static List<DiagnosticAnalyzer> LoadAnalyzers(string analyzerDirectory, string? filter = null)
    {
        var analyzers = new List<DiagnosticAnalyzer>();

        if (!Directory.Exists(analyzerDirectory))
        {
            Console.Error.WriteLine($"分析器目录不存在: {analyzerDirectory}");
            return analyzers;
        }

        foreach (var dll in Directory.GetFiles(analyzerDirectory, "*.dll", SearchOption.AllDirectories))
        {
            var fileName = Path.GetFileName(dll);
            // 只加载 AotSafety.Generator（包含所有 JCC 规则）
            if (!fileName.Contains("AotSafety.Generator", StringComparison.Ordinal))
                continue;

            try
            {
                var assembly = System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyPath(dll);
                foreach (var type in assembly.GetTypes())
                {
                    if (type.IsAbstract || type.IsGenericTypeDefinition)
                        continue;

                    if (typeof(DiagnosticAnalyzer).IsAssignableFrom(type))
                    {
                        var analyzer = (DiagnosticAnalyzer)Activator.CreateInstance(type)!;

                        // 如果指定了 filter，检查分析器是否包含该规则
                        if (!string.IsNullOrEmpty(filter) &&
                            !analyzer.SupportedDiagnostics.Any(d => d.Id == filter))
                        {
                            continue;
                        }

                        analyzers.Add(analyzer);
                        Console.WriteLine($"  加载分析器: {type.Name} (来自 {fileName})");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  加载 DLL 失败 {fileName}: {ex.Message}");
            }
        }

        return analyzers;
    }

    /// <summary>
    /// 从指定目录加载所有 CodeFixProvider DLL
    /// </summary>
    public static List<CodeFixProvider> LoadCodeFixProviders(string analyzerDirectory)
    {
        var providers = new List<CodeFixProvider>();

        if (!Directory.Exists(analyzerDirectory))
        {
            Console.Error.WriteLine($"分析器目录不存在: {analyzerDirectory}");
            return providers;
        }

        foreach (var dll in Directory.GetFiles(analyzerDirectory, "*.dll", SearchOption.AllDirectories))
        {
            var fileName = Path.GetFileName(dll);
            if (!fileName.Contains("JccCodeFixes", StringComparison.Ordinal))
                continue;

            try
            {
                var assembly = System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyPath(dll);
                foreach (var type in assembly.GetTypes())
                {
                    if (type.IsAbstract || type.IsGenericTypeDefinition)
                        continue;

                    if (typeof(CodeFixProvider).IsAssignableFrom(type))
                    {
                        var provider = (CodeFixProvider)Activator.CreateInstance(type)!;
                        providers.Add(provider);
                        Console.WriteLine($"  加载 CodeFix: {type.Name} (来自 {fileName})");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  加载 CodeFix DLL 失败 {fileName}: {ex.Message}");
            }
        }

        return providers;
    }

    /// <summary>
    /// 自动发现分析器 DLL：在项目 obj/bin 目录下搜索
    /// </summary>
    public static string FindAnalyzerDirectory(string projectRoot)
    {
        // 优先搜索 .nuget/packages/ 目录（本地打包）
        var nugetDir = Path.Combine(projectRoot, ".nuget", "packages");
        if (Directory.Exists(nugetDir))
        {
            var analyzerDlls = Directory.GetFiles(nugetDir, "AotSafety.Generator.dll", SearchOption.AllDirectories);
            if (analyzerDlls.Length > 0)
            {
                return Path.GetDirectoryName(analyzerDlls[0])!;
            }
        }

        // 搜索 lib/AotSafety.Generator 的编译输出
        var generatorDir = Path.Combine(projectRoot, "lib", "AotSafety.Generator", "src");
        if (Directory.Exists(generatorDir))
        {
            var binDir = Path.Combine(generatorDir, "bin", "Release", "netstandard2.0");
            if (Directory.Exists(binDir))
                return binDir;

            binDir = Path.Combine(generatorDir, "bin", "Debug", "netstandard2.0");
            if (Directory.Exists(binDir))
                return binDir;
        }

        return string.Empty;
    }
}
