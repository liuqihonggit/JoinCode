namespace JccAuditCli;

/// <summary>
/// jcc-audit CLI 入口
/// 用法:
///   审计: jcc-audit &lt;csproj-or-slnx-path&gt; [--analyzer-dir &lt;dir&gt;] [--output &lt;file&gt;] [--format json|text]
///   替换: jcc-audit replace &lt;csproj-or-slnx-path&gt; --rule &lt;JCC规则ID&gt; [--fix-all] [--dry-run]
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // 注册 MSBuild，确保 MSBuildWorkspace 能找到正确的构建工具
        Microsoft.Build.Locator.MSBuildLocator.RegisterDefaults();

        if (args.Length == 0 || args.Contains("--help", StringComparer.Ordinal) || args.Contains("-h", StringComparer.Ordinal))
        {
            PrintUsage();
            return 0;
        }

        // 判断子命令
        if (args[0] == "replace")
        {
            return await RunReplaceCommand(args[1..]);
        }

        if (args[0] == "audit")
        {
            // 支持 audit 子命令语法（与默认模式等价）
            return await RunAuditCommand(args[1..]);
        }

        if (args[0] == "ctor-audit")
        {
            return await RunCtorAuditCommand(args[1..]);
        }

        if (args[0] == "top-files")
        {
            return await RunTopFilesCommand(args[1..]);
        }

        // 默认: 审计模式（直接传 slnx/csproj 路径）
        return await RunAuditCommand(args);
    }

    /// <summary>
    /// 审计模式：扫描诊断并输出报告
    /// </summary>
    private static async Task<int> RunAuditCommand(string[] args)
    {
        var targetPath = args[0];
        var analyzerDir = GetArgValue(args, "--analyzer-dir") ?? string.Empty;
        var outputPath = GetArgValue(args, "--output") ?? string.Empty;
        var format = GetArgValue(args, "--format") ?? "json";
        var filter = GetArgValue(args, "--filter") ?? string.Empty;
        var skipTests = args.Contains("--skip-tests", StringComparer.Ordinal);

        // 确定项目根目录（用于搜索分析器 DLL）
        var projectRoot = FindProjectRoot(targetPath);

        // 加载分析器
        Console.WriteLine("=== JccAuditCli ===");
        Console.WriteLine($"目标: {targetPath}");
        Console.WriteLine($"项目根: {projectRoot}");

        if (string.IsNullOrEmpty(analyzerDir))
        {
            analyzerDir = AnalyzerLoader.FindAnalyzerDirectory(projectRoot);
            if (string.IsNullOrEmpty(analyzerDir))
            {
                Console.Error.WriteLine("未找到分析器 DLL。请先用 build.ps1 构建项目，或用 --analyzer-dir 指定路径。");
                return 1;
            }
        }

        Console.WriteLine($"分析器目录: {analyzerDir}");
        var analyzers = AnalyzerLoader.LoadAnalyzers(analyzerDir, filter);
        if (analyzers.Count == 0)
        {
            Console.Error.WriteLine("未加载到任何分析器。");
            return 1;
        }

        Console.WriteLine($"已加载 {analyzers.Count} 个分析器");

        // 执行审计
        var engine = new AuditEngine(analyzers);
        AuditReport report;

        var ext = Path.GetExtension(targetPath).ToLowerInvariant();
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));

        try
        {
            if (ext == ".slnx" || ext == ".sln")
            {
                report = await engine.AuditSolutionAsync(targetPath, skipTests, cts.Token);
            }
            else if (ext == ".csproj")
            {
                report = await engine.AuditProjectAsync(targetPath, cts.Token);
            }
            else
            {
                Console.Error.WriteLine($"不支持的文件类型: {ext}。请提供 .csproj 或 .slnx 文件。");
                return 1;
            }
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("审计超时（10 分钟限制）。");
            return 2;
        }

        // 过滤结果（如果指定了 --filter）
        if (!string.IsNullOrEmpty(filter))
        {
            var filteredProjects = new List<ProjectAuditResult>();
            foreach (var project in report.Projects)
            {
                var filteredDiags = project.Diagnostics.Where(d => d.RuleId == filter).ToList();
                filteredProjects.Add(project with
                {
                    Diagnostics = filteredDiags,
                    TotalDiagnostics = filteredDiags.Count,
                    WarningCount = filteredDiags.Count(d => d.Severity == "Warning"),
                    ErrorCount = filteredDiags.Count(d => d.Severity == "Error"),
                    InfoCount = filteredDiags.Count(d => d.Severity == "Info" || d.Severity == "Hidden"),
                });
            }
            report = report with
            {
                Projects = filteredProjects,
                TotalDiagnostics = filteredProjects.Sum(p => p.TotalDiagnostics),
            };
        }

        // 输出结果
        var json = JsonSerializer.Serialize(report, AuditReportContext.Default.AuditReport);

        if (!string.IsNullOrEmpty(outputPath))
        {
            await File.WriteAllTextAsync(outputPath, json);
            Console.WriteLine($"报告已写入: {outputPath}");
        }

        if (format == "text")
        {
            PrintTextReport(report);
        }
        else
        {
            // JSON 格式输出到控制台
            Console.WriteLine();
            Console.WriteLine(json);
        }

        // 返回退出码：有 Warning 则返回 3，有 Error 则返回 4，无诊断返回 0
        if (report.TotalDiagnostics == 0)
        {
            Console.WriteLine("未发现 JCC 诊断，代码质量良好。");
            return 0;
        }

        var hasError = report.Projects.Any(p => p.Diagnostics.Any(d => d.Severity == "Error"));
        var hasWarning = report.Projects.Any(p => p.Diagnostics.Any(d => d.Severity == "Warning"));
        return hasError ? 4 : hasWarning ? 3 : 0;
    }

    /// <summary>
    /// 替换模式：应用 CodeFix 到磁盘文件
    /// </summary>
    private static async Task<int> RunReplaceCommand(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help", StringComparer.Ordinal))
        {
            PrintReplaceUsage();
            return 0;
        }

        var targetPath = args[0];
        var rule = GetArgValue(args, "--rule") ?? string.Empty;
        var analyzerDir = GetArgValue(args, "--analyzer-dir") ?? string.Empty;
        var fixAll = args.Contains("--fix-all", StringComparer.Ordinal);
        var dryRun = args.Contains("--dry-run", StringComparer.Ordinal);

        if (string.IsNullOrEmpty(rule))
        {
            Console.Error.WriteLine("必须指定 --rule <JCC规则ID>，如 --rule JCC1001");
            return 1;
        }

        if (string.IsNullOrEmpty(targetPath))
        {
            Console.Error.WriteLine("必须指定目标项目或解决方案路径。");
            return 1;
        }

        var projectRoot = FindProjectRoot(targetPath);

        Console.WriteLine("=== JccAuditCli Replace ===");
        Console.WriteLine($"目标: {targetPath}");
        Console.WriteLine($"规则: {rule}");
        Console.WriteLine($"模式: {(fixAll ? "全部修复" : "逐个修复")}{(dryRun ? " (DryRun)" : "")}");

        // 加载分析器
        if (string.IsNullOrEmpty(analyzerDir))
        {
            analyzerDir = AnalyzerLoader.FindAnalyzerDirectory(projectRoot);
            if (string.IsNullOrEmpty(analyzerDir))
            {
                Console.Error.WriteLine("未找到分析器 DLL。请先用 build.ps1 构建项目，或用 --analyzer-dir 指定路径。");
                return 1;
            }
        }

        var analyzers = AnalyzerLoader.LoadAnalyzers(analyzerDir);
        if (analyzers.Count == 0)
        {
            Console.Error.WriteLine("未加载到任何分析器。");
            return 1;
        }

        // 加载 CodeFixProvider
        var codeFixProviders = AnalyzerLoader.LoadCodeFixProviders(analyzerDir);
        Console.WriteLine($"已加载 {codeFixProviders.Count} 个 CodeFixProvider");

        if (codeFixProviders.Count == 0)
        {
            Console.Error.WriteLine("未找到 CodeFixProvider。请确保 JccCodeFixes.dll 已构建。");
            return 1;
        }

        // 检查是否有匹配规则的 CodeFixProvider
        var matchingProviders = codeFixProviders.Where(p => p.FixableDiagnosticIds.Contains(rule)).ToList();
        if (matchingProviders.Count == 0)
        {
            Console.Error.WriteLine($"规则 {rule} 没有对应的 CodeFixProvider。");
            Console.Error.WriteLine($"可用的 CodeFixProvider 规则: {string.Join(", ", codeFixProviders.SelectMany(p => p.FixableDiagnosticIds).Distinct())}");
            return 1;
        }

        // 执行替换
        var engine = new ReplaceEngine(analyzers, codeFixProviders);
        var ext = Path.GetExtension(targetPath).ToLowerInvariant();
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));

        try
        {
            ReplaceResult result;
            if (ext == ".slnx" || ext == ".sln")
            {
                result = await engine.ReplaceSolutionAsync(targetPath, rule, fixAll, dryRun, cts.Token);
            }
            else if (ext == ".csproj")
            {
                result = await engine.ReplaceProjectAsync(targetPath, rule, fixAll, dryRun, cts.Token);
            }
            else
            {
                Console.Error.WriteLine($"不支持的文件类型: {ext}。请提供 .csproj 或 .slnx 文件。");
                return 1;
            }

            // 输出结果
            Console.WriteLine();
            Console.WriteLine("=== 替换结果 ===");
            foreach (var pr in result.ProjectResults)
            {
                if (pr.DiagnosticsFound == 0 && pr.FixesApplied == 0)
                    continue;

                Console.WriteLine($"[{pr.ProjectName}] 诊断: {pr.DiagnosticsFound}, 修复: {pr.FixesApplied}");
                foreach (var file in pr.ModifiedFiles)
                {
                    Console.WriteLine($"  修改: {file}");
                }
            }

            if (dryRun)
            {
                Console.WriteLine("(DryRun 模式，未实际写入文件)");
            }
            else if (result.ApplySuccess == true)
            {
                Console.WriteLine($"成功写入 {result.TotalFixesApplied} 处修复。");
            }
            else if (result.ApplySuccess == false)
            {
                Console.Error.WriteLine("应用更改失败。");
                return 1;
            }

            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("替换超时（10 分钟限制）。");
            return 2;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("jcc-audit - JCC 性能审计 CLI 工具");
        Console.WriteLine();
        Console.WriteLine("用法:");
        Console.WriteLine("  jcc-audit [audit] <csproj-or-slnx-path> [选项]  审计模式（audit可省略）");
        Console.WriteLine("  jcc-audit replace <csproj-or-slnx-path> [选项]  替换模式");
        Console.WriteLine("  jcc-audit ctor-audit <csproj-or-slnx-path> [选项]  构造函数参数审计");
        Console.WriteLine("  jcc-audit top-files <directory> [选项]            大文件排行");
        Console.WriteLine();
        Console.WriteLine("审计选项:");
        Console.WriteLine("  --analyzer-dir <dir>   分析器 DLL 目录（默认自动搜索）");
        Console.WriteLine("  --output <file>        输出 JSON 报告到文件");
        Console.WriteLine("  --format <json|text>   输出格式（默认 json）");
        Console.WriteLine("  --filter <JCC规则ID>  只输出指定规则的诊断（如 JCC3007）");
        Console.WriteLine("  --skip-tests           跳过测试/基准/Mock 项目");
        Console.WriteLine("  -h, --help             显示帮助");
        Console.WriteLine();
        Console.WriteLine("替换选项:");
        Console.WriteLine("  --rule <JCC规则ID>    要应用的规则（如 JCC1001, JCC6002）");
        Console.WriteLine("  --fix-all              应用该规则的所有修复");
        Console.WriteLine("  --dry-run              仅预览，不实际写入文件");
        Console.WriteLine("  --analyzer-dir <dir>   分析器 DLL 目录");
        Console.WriteLine();
        Console.WriteLine("构造函数审计选项:");
        Console.WriteLine("  --threshold <N>        参数数量阈值（默认 8，超过则报告）");
        Console.WriteLine("  --output <file>        输出 JSON 报告到文件");
        Console.WriteLine("  --format <json|text>   输出格式（默认 text）");
        Console.WriteLine("  --skip-tests           跳过测试/基准/Mock 项目");
        Console.WriteLine();
        Console.WriteLine("大文件排行选项:");
        Console.WriteLine("  --top <N>              返回前 N 个大文件（默认 10）");
        Console.WriteLine("  --threshold <N>        最低行数阈值（默认 200）");
        Console.WriteLine("  --output <file>        输出 JSON 报告到文件");
        Console.WriteLine("  --format <json|text>   输出格式（默认 text）");
        Console.WriteLine("  --skip-tests           跳过测试/基准/Mock 项目");
        Console.WriteLine();
        Console.WriteLine("退出码:");
        Console.WriteLine("  0  无诊断或仅 Info / 替换成功");
        Console.WriteLine("  1  参数错误");
        Console.WriteLine("  2  超时");
        Console.WriteLine("  3  有 Warning");
        Console.WriteLine("  4  有 Error");
    }

    /// <summary>
    /// 构造函数参数审计模式：扫描胖构造函数，输出报告
    /// </summary>
    private static async Task<int> RunCtorAuditCommand(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help", StringComparer.Ordinal) || args.Contains("-h", StringComparer.Ordinal))
        {
            PrintCtorAuditUsage();
            return 0;
        }

        var targetPath = args[0];
        var thresholdStr = GetArgValue(args, "--threshold") ?? "8";
        var outputPath = GetArgValue(args, "--output") ?? string.Empty;
        var format = GetArgValue(args, "--format") ?? "text";
        var skipTests = args.Contains("--skip-tests", StringComparer.Ordinal);

        if (!int.TryParse(thresholdStr, out var threshold) || threshold < 0)
        {
            Console.Error.WriteLine($"无效的阈值: {thresholdStr}，必须是正整数。");
            return 1;
        }

        if (string.IsNullOrEmpty(targetPath))
        {
            Console.Error.WriteLine("必须指定目标项目或解决方案路径。");
            return 1;
        }

        Console.WriteLine("=== JccAuditCli 构造函数参数审计 ===");
        Console.WriteLine($"目标: {targetPath}");
        Console.WriteLine($"阈值: {threshold}");
        Console.WriteLine($"跳过测试: {skipTests}");

        var engine = new AuditEngine([]);
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));

        try
        {
            var report = await engine.AuditConstructorsAsync(targetPath, threshold, skipTests, cts.Token);

            // 输出结果
            var json = JsonSerializer.Serialize(report, AuditReportContext.Default.ConstructorParamReport);

            if (!string.IsNullOrEmpty(outputPath))
            {
                await File.WriteAllTextAsync(outputPath, json);
                Console.WriteLine($"报告已写入: {outputPath}");
            }

            if (format == "json")
            {
                Console.WriteLine();
                Console.WriteLine(json);
            }
            else
            {
                PrintCtorTextReport(report);
            }

            // 退出码：有 >=12 参数的 Error 返回 4，有 >=8 的 Warning 返回 3
            var hasError = report.Constructors.Any(c => c.ParameterCount >= 12);
            var hasWarning = report.Constructors.Any(c => c.ParameterCount >= threshold);
            return hasError ? 4 : hasWarning ? 3 : 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("审计超时（10 分钟限制）。");
            return 2;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    /// <summary>
    /// 大文件排行模式：扫描指定目录下最高行数的文件
    /// </summary>
    private static async Task<int> RunTopFilesCommand(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help", StringComparer.Ordinal) || args.Contains("-h", StringComparer.Ordinal))
        {
            PrintTopFilesUsage();
            return 0;
        }

        var targetPath = args[0];
        var topNStr = GetArgValue(args, "--top") ?? "10";
        var thresholdStr = GetArgValue(args, "--threshold") ?? "200";
        var outputPath = GetArgValue(args, "--output") ?? string.Empty;
        var format = GetArgValue(args, "--format") ?? "text";
        var skipTests = args.Contains("--skip-tests", StringComparer.Ordinal);

        if (!int.TryParse(topNStr, out var topN) || topN <= 0)
        {
            Console.Error.WriteLine($"无效的 Top N: {topNStr}，必须是正整数。");
            return 1;
        }

        if (!int.TryParse(thresholdStr, out var threshold) || threshold < 0)
        {
            Console.Error.WriteLine($"无效的阈值: {thresholdStr}，必须是非负整数。");
            return 1;
        }

        Console.WriteLine("=== JccAuditCli 大文件排行 ===");
        Console.WriteLine($"目录: {Path.GetFullPath(targetPath)}");
        Console.WriteLine($"Top: {topN}");
        Console.WriteLine($"阈值: {threshold} 行");
        Console.WriteLine($"跳过测试: {skipTests}");

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));

        try
        {
            var report = FileLineCounter.Scan(targetPath, topN, threshold, skipTests, cts.Token);

            var json = JsonSerializer.Serialize(report, AuditReportContext.Default.FileLineReport);

            if (!string.IsNullOrEmpty(outputPath))
            {
                await File.WriteAllTextAsync(outputPath, json, cts.Token);
                Console.WriteLine($"报告已写入: {outputPath}");
            }

            if (format == "json")
            {
                Console.WriteLine();
                Console.WriteLine(json);
            }
            else
            {
                PrintTopFilesTextReport(report);
            }

            return report.Files.Count > 0 ? 3 : 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("扫描超时（5 分钟限制）。");
            return 2;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void PrintTopFilesTextReport(FileLineReport report)
    {
        Console.WriteLine();
        Console.WriteLine("=== 大文件排行报告 ===");
        Console.WriteLine($"目录: {report.RootPath}");
        Console.WriteLine($"时间: {report.Timestamp:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"Top: {report.TopN}");
        Console.WriteLine($"阈值: {report.Threshold} 行");
        Console.WriteLine($"总 .cs 文件: {report.TotalCsFiles}");
        Console.WriteLine($"跳过文件: {report.SkippedFiles}");
        Console.WriteLine($"超过阈值: {report.FilesAboveThreshold} 个");
        Console.WriteLine();

        if (report.Files.Count == 0)
        {
            Console.WriteLine($"未发现超过 {report.Threshold} 行的文件，代码组织良好。");
            return;
        }

        var maxLineDigits = report.Files[0].LineCount.ToString().Length;

        for (var i = 0; i < report.Files.Count; i++)
        {
            var file = report.Files[i];
            var severity = file.LineCount >= 2000 ? "!!" : file.LineCount >= 1000 ? "! " : "  ";
            Console.WriteLine($"  {severity} {i + 1,2}. {file.LineCount.ToString().PadLeft(maxLineDigits)} 行 - {file.FilePath}");
        }

        Console.WriteLine();
        Console.WriteLine("  !! = 超过2000行（紧急拆分）  ! = 超过1000行（建议拆分）");
    }

    private static void PrintTopFilesUsage()
    {
        Console.WriteLine("jcc-audit top-files - 大文件排行");
        Console.WriteLine();
        Console.WriteLine("用法: jcc-audit top-files <directory> [选项]");
        Console.WriteLine();
        Console.WriteLine("参数:");
        Console.WriteLine("  <directory>            扫描目录路径");
        Console.WriteLine();
        Console.WriteLine("选项:");
        Console.WriteLine("  --top <N>              返回前 N 个大文件（默认 10）");
        Console.WriteLine("  --threshold <N>        最低行数阈值（默认 200）");
        Console.WriteLine("  --output <file>        输出 JSON 报告到文件");
        Console.WriteLine("  --format <json|text>   输出格式（默认 text）");
        Console.WriteLine("  --skip-tests           跳过测试/基准/Mock 项目");
        Console.WriteLine();
        Console.WriteLine("示例:");
        Console.WriteLine("  jcc-audit top-files .");
        Console.WriteLine("  jcc-audit top-files . --top 20 --threshold 500 --skip-tests");
        Console.WriteLine("  jcc-audit top-files ./src --format json --output top-files.json");
    }

    private static void PrintCtorAuditUsage()
    {
        Console.WriteLine("jcc-audit ctor-audit - 构造函数参数审计");
        Console.WriteLine();
        Console.WriteLine("用法: jcc-audit ctor-audit <csproj-or-slnx-path> [选项]");
        Console.WriteLine();
        Console.WriteLine("参数:");
        Console.WriteLine("  <csproj-or-slnx-path>  目标项目或解决方案路径");
        Console.WriteLine();
        Console.WriteLine("选项:");
        Console.WriteLine("  --threshold <N>        参数数量阈值（默认 8，超过则报告）");
        Console.WriteLine("  --output <file>        输出 JSON 报告到文件");
        Console.WriteLine("  --format <json|text>   输出格式（默认 text）");
        Console.WriteLine("  --skip-tests           跳过测试/基准/Mock 项目");
        Console.WriteLine();
        Console.WriteLine("示例:");
        Console.WriteLine("  jcc-audit ctor-audit JoinCode.slnx");
        Console.WriteLine("  jcc-audit ctor-audit JoinCode.slnx --threshold 5 --skip-tests");
        Console.WriteLine("  jcc-audit ctor-audit Brain.csproj --format json --output ctor-report.json");
    }

    private static void PrintCtorTextReport(ConstructorParamReport report)
    {
        Console.WriteLine();
        Console.WriteLine("=== 构造函数参数审计报告 ===");
        Console.WriteLine($"目标: {report.TargetPath}");
        Console.WriteLine($"时间: {report.Timestamp:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"阈值: {report.Threshold}");
        Console.WriteLine($"胖构造函数总数: {report.TotalFatCtors}");
        Console.WriteLine();

        if (report.Constructors.Count == 0)
        {
            Console.WriteLine("未发现胖构造函数，代码结构良好。");
            return;
        }

        // 按参数数量降序排列，分组展示
        foreach (var ctor in report.Constructors)
        {
            var severity = ctor.ParameterCount >= 12 ? "ERROR" : "WARN";
            Console.WriteLine($"  [{severity}] {ctor.ParameterCount} 个参数 - {ctor.ClassName}");
            Console.WriteLine($"    文件: {ctor.FilePath}:{ctor.LineNumber}");
            Console.WriteLine($"    签名: {ctor.ConstructorSignature}");
            Console.WriteLine($"    参数: {string.Join(", ", ctor.ParameterTypes)}");
            Console.WriteLine($"    建议: 考虑将相关参数聚合为中间件上下文，减少构造函数注入");
            Console.WriteLine();
        }
    }

    private static void PrintReplaceUsage()
    {
        Console.WriteLine("jcc-audit replace - AST 批量替换");
        Console.WriteLine();
        Console.WriteLine("用法: jcc-audit replace <csproj-or-slnx-path> --rule <JCC规则ID> [选项]");
        Console.WriteLine();
        Console.WriteLine("参数:");
        Console.WriteLine("  <csproj-or-slnx-path>  目标项目或解决方案路径");
        Console.WriteLine();
        Console.WriteLine("选项:");
        Console.WriteLine("  --rule <JCC规则ID>    要应用的规则（必须）");
        Console.WriteLine("  --fix-all              应用该规则的所有修复");
        Console.WriteLine("  --dry-run              仅预览，不实际写入文件");
        Console.WriteLine("  --analyzer-dir <dir>   分析器 DLL 目录");
        Console.WriteLine();
        Console.WriteLine("示例:");
        Console.WriteLine("  jcc-audit replace MyProject.csproj --rule JCC1001 --fix-all");
        Console.WriteLine("  jcc-audit replace JoinCode.slnx --rule JCC6002 --dry-run");
    }

    private static void PrintTextReport(AuditReport report)
    {
        Console.WriteLine();
        Console.WriteLine($"=== 审计报告 ===");
        Console.WriteLine($"目标: {report.TargetPath}");
        Console.WriteLine($"时间: {report.Timestamp:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"项目数: {report.TotalProjects}");
        Console.WriteLine($"诊断总数: {report.TotalDiagnostics}");
        Console.WriteLine();

        foreach (var project in report.Projects)
        {
            if (project.TotalDiagnostics == 0)
                continue;

            Console.WriteLine($"[{project.ProjectName}] {project.TotalDiagnostics} 条诊断 (W:{project.WarningCount} E:{project.ErrorCount} I:{project.InfoCount})");

            foreach (var diag in project.Diagnostics)
            {
                Console.WriteLine($"  {diag.RuleId} [{diag.Severity}] {diag.FilePath}:{diag.Line}:{diag.Column}");
                Console.WriteLine($"    {diag.Message}");
            }

            Console.WriteLine();
        }
    }

    private static string? GetArgValue(string[] args, string key)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == key)
                return args[i + 1];
        }

        return null;
    }

    private static string FindProjectRoot(string targetPath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(targetPath));

        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "JoinCode.slnx")) ||
                File.Exists(Path.Combine(dir, "JoinCode.sln")))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        return Path.GetDirectoryName(Path.GetFullPath(targetPath))!;
    }
}
