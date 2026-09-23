namespace JccAuditCli;

/// <summary>
/// jcc-audit CLI 入口
/// 用法:
///   审计: jcc-audit &lt;csproj-or-slnx-path&gt; [--analyzer-dir &lt;dir&gt;] [--output &lt;file&gt;] [--format json|text]
///   替换: jcc-audit replace &lt;csproj-or-slnx-path&gt; --rule &lt;JCC规则ID&gt; [--fix-all] [--dry-run]
/// </summary>
public static class Program {
    /// <summary>
    /// 程序入口。
    /// </summary>
    /// <param name="args">命令行参数。</param>
    public static async Task<int> Main(string[] args) {
        // 注册 MSBuild，确保 MSBuildWorkspace 能找到正确的构建工具
        Microsoft.Build.Locator.MSBuildLocator.RegisterDefaults();

        if (args.Length == 0 || args.Contains("--help", StringComparer.Ordinal) || args.Contains("-h", StringComparer.Ordinal)) {
            PrintUsage();
            return 0;
        }

        // 判断子命令
        if (args[0] == "replace") {
            return await RunReplaceCommand(args[1..]);
        }

        if (args[0] == "audit") {
            // 支持 audit 子命令语法（与默认模式等价）
            return await RunAuditCommand(args[1..]);
        }

        if (args[0] == "ctor-audit") {
            return await RunCtorAuditCommand(args[1..]);
        }

        if (args[0] == "top-files") {
            return await RunTopFilesCommand(args[1..]);
        }

        if (args[0] == "layer-audit") {
            return await RunLayerAuditCommand(args[1..]);
        }

        if (args[0] == "strip-bom") {
            return await RunStripBomCommand(args[1..]).ConfigureAwait(false);
        }

        if (args[0] == "fix-async") {
            return await RunFixAsyncCommand(args[1..]).ConfigureAwait(false);
        }

        if (args[0] == "fix-async-issues") {
            return await RunFixAsyncIssuesCommand(args[1..]).ConfigureAwait(false);
        }

        if (args[0] == "fix-from-build-errors") {
            return await RunFixFromBuildErrorsCommand(args[1..]).ConfigureAwait(false);
        }

        if (args[0] == "fix-getawaiter-getresult") {
            return await RunFixGetAwaiterGetResultCommand(args[1..]).ConfigureAwait(false);
        }

        if (args[0] == "fix-jcc3016") {
            return await RunFixJcc3016Command(args[1..]).ConfigureAwait(false);
        }

        if (args[0] == "analyze-getawaiter") {
            return await RunAnalyzeGetAwaiterCommand(args[1..]).ConfigureAwait(false);
        }

        if (args[0] == "fix-disposable-async") {
            return await RunFixDisposableDirectionCommand(args[1..], FixDirection.Async).ConfigureAwait(false);
        }

        if (args[0] == "fix-disposable-sync") {
            return await RunFixDisposableDirectionCommand(args[1..], FixDirection.Sync).ConfigureAwait(false);
        }

        if (args[0] == "sync-async") {
            return await RunSyncAsyncCommand(args[1..]).ConfigureAwait(false);
        }

        if (args[0] == "cascade-fix") {
            return await RunCascadeFixCommand(args[1..]).ConfigureAwait(false);
        }

        // 默认: 审计模式（直接传 slnx/csproj 路径）
        return await RunAuditCommand(args);
    }

    /// <summary>
    /// 审计模式：扫描诊断并输出报告
    /// </summary>
    private static async Task<int> RunAuditCommand(string[] args) {
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

        if (string.IsNullOrEmpty(analyzerDir)) {
            analyzerDir = AnalyzerLoader.FindAnalyzerDirectory(projectRoot);
            if (string.IsNullOrEmpty(analyzerDir)) {
                Console.Error.WriteLine("未找到分析器 DLL。请先用 build.ps1 构建项目，或用 --analyzer-dir 指定路径。");
                return 1;
            }
        }

        Console.WriteLine($"分析器目录: {analyzerDir}");
        var analyzers = AnalyzerLoader.LoadAnalyzers(analyzerDir, filter);
        if (analyzers.Count == 0) {
            Console.Error.WriteLine("未加载到任何分析器。");
            return 1;
        }

        Console.WriteLine($"已加载 {analyzers.Count} 个分析器");

        // 执行审计
        var engine = new AuditEngine(analyzers);
        AuditReport report;

        var ext = Path.GetExtension(targetPath).ToLowerInvariant();
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));

        try {
            if (ext == ".slnx" || ext == ".sln") {
                report = await engine.AuditSolutionAsync(targetPath, skipTests, cts.Token);
            } else if (ext == ".csproj") {
                report = await engine.AuditProjectAsync(targetPath, cts.Token);
            } else {
                Console.Error.WriteLine($"不支持的文件类型: {ext}。请提供 .csproj 或 .slnx 文件。");
                return 1;
            }
        } catch (OperationCanceledException) {
            Console.Error.WriteLine("审计超时（10 分钟限制）。");
            return 2;
        }

        // 过滤结果（如果指定了 --filter）
        if (!string.IsNullOrEmpty(filter)) {
            var filteredProjects = new List<ProjectAuditResult>();
            foreach (var project in report.Projects) {
                var filteredDiags = project.Diagnostics.Where(d => d.RuleId == filter).ToList();
                filteredProjects.Add(project with {
                    Diagnostics = filteredDiags,
                    TotalDiagnostics = filteredDiags.Count,
                    WarningCount = filteredDiags.Count(d => d.Severity == "Warning"),
                    ErrorCount = filteredDiags.Count(d => d.Severity == "Error"),
                    InfoCount = filteredDiags.Count(d => d.Severity == "Info" || d.Severity == "Hidden"),
                });
            }
            report = report with {
                Projects = filteredProjects,
                TotalDiagnostics = filteredProjects.Sum(p => p.TotalDiagnostics),
            };
        }

        // 输出结果
        var json = JsonSerializer.Serialize(report, AuditReportContext.Default.AuditReport);

        // 默认日志：未指定 --output 时自动生成 audit-report-{时间戳}.json
        var outputWasDefaulted = string.IsNullOrEmpty(outputPath);
        if (outputWasDefaulted) {
            var ts = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            outputPath = $"audit-report-{ts}.json";
        }
        var fullOutputPath = Path.GetFullPath(outputPath);
        await SafeFileIO.WriteAllTextAsync(outputPath, json);
        Console.WriteLine($"[日志路径] 报告已写入: {fullOutputPath}{(outputWasDefaulted ? " (自动生成)" : "")}");
        Console.WriteLine($"[日志路径] AI 可读取此文件获取完整诊断报告");

        if (format == "text") {
            PrintTextReport(report);
        }

        // 返回退出码：有 Warning 则返回 3，有 Error 则返回 4，无诊断返回 0
        if (report.TotalDiagnostics == 0) {
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
    private static async Task<int> RunReplaceCommand(string[] args) {
        if (args.Length == 0 || args.Contains("--help", StringComparer.Ordinal)) {
            PrintReplaceUsage();
            return 0;
        }

        var targetPath = args[0];
        var rule = GetArgValue(args, "--rule") ?? string.Empty;
        var analyzerDir = GetArgValue(args, "--analyzer-dir") ?? string.Empty;
        var fixAll = args.Contains("--fix-all", StringComparer.Ordinal);
        var dryRun = args.Contains("--dry-run", StringComparer.Ordinal);

        if (string.IsNullOrEmpty(rule)) {
            Console.Error.WriteLine("必须指定 --rule <JCC规则ID>，如 --rule JCC1001");
            return 1;
        }

        if (string.IsNullOrEmpty(targetPath)) {
            Console.Error.WriteLine("必须指定目标项目或解决方案路径。");
            return 1;
        }

        var projectRoot = FindProjectRoot(targetPath);

        Console.WriteLine("=== JccAuditCli Replace ===");
        Console.WriteLine($"目标: {targetPath}");
        Console.WriteLine($"规则: {rule}");
        Console.WriteLine($"模式: {(fixAll ? "全部修复" : "逐个修复")}{(dryRun ? " (DryRun)" : "")}");

        // 加载分析器
        if (string.IsNullOrEmpty(analyzerDir)) {
            analyzerDir = AnalyzerLoader.FindAnalyzerDirectory(projectRoot);
            if (string.IsNullOrEmpty(analyzerDir)) {
                Console.Error.WriteLine("未找到分析器 DLL。请先用 build.ps1 构建项目，或用 --analyzer-dir 指定路径。");
                return 1;
            }
        }

        var analyzers = AnalyzerLoader.LoadAnalyzers(analyzerDir);
        if (analyzers.Count == 0) {
            Console.Error.WriteLine("未加载到任何分析器。");
            return 1;
        }

        // 加载 CodeFixProvider
        var codeFixProviders = AnalyzerLoader.LoadCodeFixProviders(analyzerDir);
        Console.WriteLine($"已加载 {codeFixProviders.Count} 个 CodeFixProvider");

        if (codeFixProviders.Count == 0) {
            Console.Error.WriteLine("未找到 CodeFixProvider。请确保 CodeFixes.dll 已构建。");
            return 1;
        }

        // 检查是否有匹配规则的 CodeFixProvider
        var matchingProviders = codeFixProviders.Where(p => p.FixableDiagnosticIds.Contains(rule)).ToList();
        if (matchingProviders.Count == 0) {
            Console.Error.WriteLine($"规则 {rule} 没有对应的 CodeFixProvider。");
            Console.Error.WriteLine($"可用的 CodeFixProvider 规则: {string.Join(", ", codeFixProviders.SelectMany(p => p.FixableDiagnosticIds).Distinct())}");
            return 1;
        }

        // 执行替换
        var engine = new ReplaceEngine(analyzers, codeFixProviders);
        var ext = Path.GetExtension(targetPath).ToLowerInvariant();
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));

        try {
            ReplaceResult result;
            if (ext == ".slnx" || ext == ".sln") {
                result = await engine.ReplaceSolutionAsync(targetPath, rule, fixAll, dryRun, cts.Token);
            } else if (ext == ".csproj") {
                result = await engine.ReplaceProjectAsync(targetPath, rule, fixAll, dryRun, cts.Token);
            } else {
                Console.Error.WriteLine($"不支持的文件类型: {ext}。请提供 .csproj 或 .slnx 文件。");
                return 1;
            }

            // 输出结果
            Console.WriteLine();
            Console.WriteLine("=== 替换结果 ===");
            foreach (var pr in result.ProjectResults) {
                if (pr.DiagnosticsFound == 0 && pr.FixesApplied == 0)
                    continue;

                Console.WriteLine($"[{pr.ProjectName}] 诊断: {pr.DiagnosticsFound}, 修复: {pr.FixesApplied}");
                foreach (var file in pr.ModifiedFiles) {
                    Console.WriteLine($"  修改: {file}");
                }
            }

            if (dryRun) {
                Console.WriteLine("(DryRun 模式，未实际写入文件)");
            } else if (result.ApplySuccess == true) {
                Console.WriteLine($"成功写入 {result.TotalFixesApplied} 处修复。");
            } else if (result.ApplySuccess == false) {
                Console.Error.WriteLine("应用更改失败。");
                return 1;
            }

            return 0;
        } catch (OperationCanceledException) {
            Console.Error.WriteLine("替换超时（10 分钟限制）。");
            return 2;
        }
    }

    private static void PrintUsage() {
        Console.WriteLine("jcc-audit - JCC 性能审计 CLI 工具");
        Console.WriteLine();
        Console.WriteLine("子命令按功能分三组：审计(Audit) / 修复(Fix) / 统计(Stats)");
        Console.WriteLine();
        Console.WriteLine("=== 审计组（Audit）— 扫描诊断，不修改文件 ===");
        Console.WriteLine("  jcc-audit [audit] <csproj-or-slnx> [选项]        JCC 规则审计（audit可省略）");
        Console.WriteLine("  jcc-audit ctor-audit <csproj-or-slnx> [选项]    构造函数参数审计");
        Console.WriteLine("  jcc-audit layer-audit <slnx> [选项]             层依赖审计");
        Console.WriteLine();
        Console.WriteLine("=== 修复组（Fix）— 修改源码文件 ===");
        Console.WriteLine("  jcc-audit replace <csproj-or-slnx> [选项]       AST 批量替换（应用 CodeFix）");
        Console.WriteLine("  jcc-audit strip-bom <directory> [选项]          移除 .cs 文件 UTF-8 BOM");
        Console.WriteLine("  jcc-audit sync-async <slnx> [选项]              async/await 同步化（移除 async，改签名）");
        Console.WriteLine("  jcc-audit fix-jcc3016 <slnx|sln|csproj> [选项]  JCC3016 检测+修复（共享检测器）");
        Console.WriteLine();
        Console.WriteLine("=== 统计组（Stats）— 信息查询排行 ===");
        Console.WriteLine("  jcc-audit top-files <directory> [选项]          大文件排行");
        Console.WriteLine();
        Console.WriteLine("通用选项:");
        Console.WriteLine("  --output <file>        输出 JSON 报告到文件");
        Console.WriteLine("  --format <json|text>   输出格式");
        Console.WriteLine("  --skip-tests           跳过测试/基准/Mock 项目");
        Console.WriteLine("  --dry-run              仅预览，不实际写入文件（replace/strip-bom）");
        Console.WriteLine("  -h, --help             显示帮助");
        Console.WriteLine();
        Console.WriteLine("审计组专属选项:");
        Console.WriteLine("  --analyzer-dir <dir>   分析器 DLL 目录（默认自动搜索）");
        Console.WriteLine("  --filter <JCC规则ID>  只输出指定规则的诊断（如 JCC3007，audit 专用）");
        Console.WriteLine("  --threshold <N>        参数数量阈值（ctor-audit 默认 8 / top-files 默认 200）");
        Console.WriteLine();
        Console.WriteLine("修复组专属选项:");
        Console.WriteLine("  --rule <JCC规则ID>    要应用的规则（如 JCC1001, JCC6002，replace 必须指定）");
        Console.WriteLine("  --fix-all              应用该规则的所有修复（replace）");
        Console.WriteLine();
        Console.WriteLine("统计组专属选项:");
        Console.WriteLine("  --top <N>              返回前 N 个大文件（top-files 默认 10）");
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
    private static async Task<int> RunCtorAuditCommand(string[] args) {
        if (args.Length == 0 || args.Contains("--help", StringComparer.Ordinal) || args.Contains("-h", StringComparer.Ordinal)) {
            PrintCtorAuditUsage();
            return 0;
        }

        var targetPath = args[0];
        var thresholdStr = GetArgValue(args, "--threshold") ?? "8";
        var outputPath = GetArgValue(args, "--output") ?? string.Empty;
        var format = GetArgValue(args, "--format") ?? "text";
        var skipTests = args.Contains("--skip-tests", StringComparer.Ordinal);

        if (!int.TryParse(thresholdStr, out var threshold) || threshold < 0) {
            Console.Error.WriteLine($"无效的阈值: {thresholdStr}，必须是正整数。");
            return 1;
        }

        if (string.IsNullOrEmpty(targetPath)) {
            Console.Error.WriteLine("必须指定目标项目或解决方案路径。");
            return 1;
        }

        Console.WriteLine("=== JccAuditCli 构造函数参数审计 ===");
        Console.WriteLine($"目标: {targetPath}");
        Console.WriteLine($"阈值: {threshold}");
        Console.WriteLine($"跳过测试: {skipTests}");

        var engine = new AuditEngine([]);
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));

        try {
            var report = await engine.AuditConstructorsAsync(targetPath, threshold, skipTests, cts.Token);

            // 输出结果
            var json = JsonSerializer.Serialize(report, AuditReportContext.Default.ConstructorParamReport);

            if (!string.IsNullOrEmpty(outputPath)) {
                await SafeFileIO.WriteAllTextAsync(outputPath, json);
                Console.WriteLine($"报告已写入: {outputPath}");
            }

            if (format == "json") {
                Console.WriteLine();
                Console.WriteLine(json);
            } else {
                PrintCtorTextReport(report);
            }

            // 退出码：有 >=12 参数的 Error 返回 4，有 >=8 的 Warning 返回 3
            var hasError = report.Constructors.Any(c => c.ParameterCount >= 12);
            var hasWarning = report.Constructors.Any(c => c.ParameterCount >= threshold);
            return hasError ? 4 : hasWarning ? 3 : 0;
        } catch (OperationCanceledException) {
            Console.Error.WriteLine("审计超时（10 分钟限制）。");
            return 2;
        } catch (ArgumentException ex) {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    /// <summary>
    /// 大文件排行模式：扫描指定目录下最高行数的文件
    /// </summary>
    private static async Task<int> RunTopFilesCommand(string[] args) {
        if (args.Length == 0 || args.Contains("--help", StringComparer.Ordinal) || args.Contains("-h", StringComparer.Ordinal)) {
            PrintTopFilesUsage();
            return 0;
        }

        var targetPath = args[0];
        var topNStr = GetArgValue(args, "--top") ?? "10";
        var thresholdStr = GetArgValue(args, "--threshold") ?? "200";
        var outputPath = GetArgValue(args, "--output") ?? string.Empty;
        var format = GetArgValue(args, "--format") ?? "text";
        var skipTests = args.Contains("--skip-tests", StringComparer.Ordinal);

        if (!int.TryParse(topNStr, out var topN) || topN <= 0) {
            Console.Error.WriteLine($"无效的 Top N: {topNStr}，必须是正整数。");
            return 1;
        }

        if (!int.TryParse(thresholdStr, out var threshold) || threshold < 0) {
            Console.Error.WriteLine($"无效的阈值: {thresholdStr}，必须是非负整数。");
            return 1;
        }

        Console.WriteLine("=== JccAuditCli 大文件排行 ===");
        Console.WriteLine($"目录: {Path.GetFullPath(targetPath)}");
        Console.WriteLine($"Top: {topN}");
        Console.WriteLine($"阈值: {threshold} 行");
        Console.WriteLine($"跳过测试: {skipTests}");

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));

        try {
            var report = FileLineCounter.Scan(targetPath, topN, threshold, skipTests, cts.Token);

            var json = JsonSerializer.Serialize(report, AuditReportContext.Default.FileLineReport);

            if (!string.IsNullOrEmpty(outputPath)) {
                await SafeFileIO.WriteAllTextAsync(outputPath, json, cts.Token);
                Console.WriteLine($"报告已写入: {outputPath}");
            }

            if (format == "json") {
                Console.WriteLine();
                Console.WriteLine(json);
            } else {
                PrintTopFilesTextReport(report);
            }

            return report.Files.Count > 0 ? 3 : 0;
        } catch (OperationCanceledException) {
            Console.Error.WriteLine("扫描超时（5 分钟限制）。");
            return 2;
        } catch (ArgumentException ex) {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    /// <summary>
    /// 层依赖审计模式：检测七层架构违规引用
    /// </summary>
    private static async Task<int> RunLayerAuditCommand(string[] args) {
        if (args.Length == 0 || args.Contains("--help", StringComparer.Ordinal) || args.Contains("-h", StringComparer.Ordinal)) {
            Console.WriteLine("用法: jcc-audit layer-audit <slnx-path> [--format json|text] [--output <file>] [--skip-tests]");
            return 0;
        }

        var targetPath = args[0];
        var outputPath = GetArgValue(args, "--output") ?? string.Empty;
        var format = GetArgValue(args, "--format") ?? "text";
        var skipTests = args.Contains("--skip-tests", StringComparer.Ordinal);

        if (string.IsNullOrEmpty(targetPath)) {
            Console.Error.WriteLine("必须指定解决方案路径。");
            return 1;
        }

        Console.WriteLine("=== JccAuditCli 层依赖审计 ===");
        Console.WriteLine($"目标: {targetPath}");
        Console.WriteLine($"跳过测试: {skipTests}");

        var engine = new AuditEngine([]);
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));

        try {
            var report = await engine.AuditLayersAsync(targetPath, skipTests, cts.Token);

            var json = JsonSerializer.Serialize(report, AuditReportContext.Default.LayerAuditReport);

            if (!string.IsNullOrEmpty(outputPath)) {
                await SafeFileIO.WriteAllTextAsync(outputPath, json);
                Console.WriteLine($"报告已写入: {outputPath}");
            }

            if (format == "json") {
                Console.WriteLine();
                Console.WriteLine(json);
            } else {
                PrintLayerAuditTextReport(report);
            }

            return report.ErrorCount > 0 ? 4 : report.TotalViolations > 0 ? 3 : 0;
        } catch (OperationCanceledException) {
            Console.Error.WriteLine("审计超时（10 分钟限制）。");
            return 2;
        } catch (ArgumentException ex) {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    /// <summary>
    /// BOM 移除模式：扫描指定目录下所有 .cs 文件，移除 UTF-8 BOM
    /// </summary>
    private static async Task<int> RunStripBomCommand(string[] args) {
        if (args.Length == 0 || args.Contains("--help", StringComparer.Ordinal) || args.Contains("-h", StringComparer.Ordinal)) {
            PrintStripBomUsage();
            return 0;
        }

        var targetPath = args[0];
        var outputPath = GetArgValue(args, "--output") ?? string.Empty;
        var format = GetArgValue(args, "--format") ?? "text";
        var dryRun = args.Contains("--dry-run", StringComparer.Ordinal);
        var skipTests = args.Contains("--skip-tests", StringComparer.Ordinal);

        if (string.IsNullOrEmpty(targetPath)) {
            Console.Error.WriteLine("必须指定扫描目录路径。");
            return 1;
        }

        Console.WriteLine("=== JccAuditCli BOM 移除 ===");
        Console.WriteLine($"目录: {Path.GetFullPath(targetPath)}");
        Console.WriteLine($"模式: {(dryRun ? "预览 (DryRun)" : "实际写入")}");
        Console.WriteLine($"跳过测试: {skipTests}");

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));

        try {
            var report = await BomStripper.Strip(targetPath, dryRun, skipTests, cts.Token).ConfigureAwait(false);

            var json = JsonSerializer.Serialize(report, AuditReportContext.Default.BomStripReport);

            if (!string.IsNullOrEmpty(outputPath)) {
                await SafeFileIO.WriteAllTextAsync(outputPath, json).ConfigureAwait(false);
                Console.WriteLine($"报告已写入: {outputPath}");
            }

            if (format == "json") {
                Console.WriteLine();
                Console.WriteLine(json);
            } else {
                PrintStripBomTextReport(report);
            }

            return report.StrippedCount > 0 ? 3 : 0;
        } catch (OperationCanceledException) {
            Console.Error.WriteLine("扫描超时（5 分钟限制）。");
            return 2;
        } catch (ArgumentException ex) {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    /// <summary>
    /// fix-async 模式：用 Roslyn AST 检测含 await 的非 async 方法，自动添加 async 修饰符
    /// </summary>
    private static async Task<int> RunFixAsyncCommand(string[] args) {
        if (args.Length == 0 || args.Contains("--help", StringComparer.Ordinal) || args.Contains("-h", StringComparer.Ordinal)) {
            Console.WriteLine("用法: jcc-audit fix-async &lt;目录&gt; [--dry-run] [--skip-tests]");
            Console.WriteLine();
            Console.WriteLine("选项:");
            Console.WriteLine("  --dry-run     仅预览，不实际写入文件");
            Console.WriteLine("  --skip-tests  跳过测试项目目录");
            return 0;
        }

        var targetPath = args[0];
        var dryRun = args.Contains("--dry-run", StringComparer.Ordinal);
        var skipTests = args.Contains("--skip-tests", StringComparer.Ordinal);

        if (string.IsNullOrEmpty(targetPath)) {
            Console.Error.WriteLine("必须指定扫描目录路径。");
            return 1;
        }

        Console.WriteLine("=== JccAuditCli fix-async ===");
        Console.WriteLine($"目录: {Path.GetFullPath(targetPath)}");
        Console.WriteLine($"模式: {(dryRun ? "预览 (DryRun)" : "实际写入")}");
        Console.WriteLine($"跳过测试: {skipTests}");

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));

        try {
            var (fixedFiles, fixedMethods, skippedFiles) = await AsyncMethodFixer.FixDirectoryAsync(targetPath, dryRun, skipTests, cts.Token).ConfigureAwait(false);

            Console.WriteLine();
            Console.WriteLine("=== fix-async 报告 ===");
            Console.WriteLine($"修复文件: {fixedFiles}");
            Console.WriteLine($"修复方法: {fixedMethods}");
            Console.WriteLine($"跳过文件: {skippedFiles}");

            return fixedMethods > 0 ? (dryRun ? 3 : 0) : 0;
        } catch (OperationCanceledException) {
            Console.Error.WriteLine("扫描超时（5 分钟限制）。");
            return 2;
        } catch (ArgumentException ex) {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    /// <summary>
    /// fix-async-issues 模式：用 MSBuildWorkspace 语义模型一次性修复所有异步相关问题
    /// </summary>
    private static async Task<int> RunFixAsyncIssuesCommand(string[] args) {
        if (args.Length == 0 || args.Contains("--help", StringComparer.Ordinal) || args.Contains("-h", StringComparer.Ordinal)) {
            Console.WriteLine("用法: jcc-audit fix-async-issues &lt;slnx&gt; [--dry-run]");
            Console.WriteLine();
            Console.WriteLine("修复类型:");
            Console.WriteLine("  1. 方法重命名 Clear→ClearAsync + 加 await");
            Console.WriteLine("  2. 移除测试代码中 ConfigureAwait(false)");
            Console.WriteLine("  3. 加 await 到未 await 的 Task/ValueTask 调用");
            Console.WriteLine("  4. using → await using（IAsyncDisposable）");
            Console.WriteLine("  5. 修复 await xxx.Should() → (await xxx).Should()");
            return 0;
        }

        var targetPath = args[0];
        var dryRun = args.Contains("--dry-run", StringComparer.Ordinal);

        if (string.IsNullOrEmpty(targetPath)) {
            Console.Error.WriteLine("必须指定解决方案路径。");
            return 1;
        }

        Console.WriteLine("=== JccAuditCli fix-async-issues ===");
        Console.WriteLine($"解决方案: {Path.GetFullPath(targetPath)}");
        Console.WriteLine($"模式: {(dryRun ? "预览 (DryRun)" : "实际写入")}");

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));

        try {
            var (fixedFiles, fixedIssues, skippedFiles) = await AsyncIssueFixer.FixAllAsync(targetPath, dryRun, cts.Token).ConfigureAwait(false);

            Console.WriteLine();
            Console.WriteLine("=== fix-async-issues 报告 ===");
            Console.WriteLine($"修复文件: {fixedFiles}");
            Console.WriteLine($"修复问题: {fixedIssues}");
            Console.WriteLine($"跳过文件: {skippedFiles}");

            return fixedIssues > 0 ? (dryRun ? 3 : 0) : 0;
        } catch (OperationCanceledException) {
            Console.Error.WriteLine("扫描超时（10 分钟限制）。");
            return 2;
        } catch (ArgumentException ex) {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    /// <summary>
    /// fix-from-build-errors 模式：运行 dotnet build，解析 CS1503/CS1061 错误位置，精确加 await
    /// </summary>
    private static async Task<int> RunFixFromBuildErrorsCommand(string[] args) {
        if (args.Length == 0 || args.Contains("--help", StringComparer.Ordinal)) {
            Console.WriteLine("用法: jcc-audit fix-from-build-errors &lt;slnx&gt; [--dry-run]");
            return 0;
        }

        var targetPath = args[0];
        var dryRun = args.Contains("--dry-run", StringComparer.Ordinal);

        Console.WriteLine("=== JccAuditCli fix-from-build-errors ===");
        Console.WriteLine($"解决方案: {Path.GetFullPath(targetPath)}");
        Console.WriteLine($"模式: {(dryRun ? "预览 (DryRun)" : "实际写入")}");

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(15));

        try {
            var (fixedFiles, fixedIssues) = await BuildErrorFixer.FixFromBuildErrorsAsync(targetPath, dryRun, cts.Token).ConfigureAwait(false);

            Console.WriteLine();
            Console.WriteLine("=== fix-from-build-errors 报告 ===");
            Console.WriteLine($"修复文件: {fixedFiles}");
            Console.WriteLine($"修复问题: {fixedIssues}");

            return fixedIssues > 0 ? (dryRun ? 3 : 0) : 0;
        } catch (OperationCanceledException) {
            Console.Error.WriteLine("超时。");
            return 2;
        }
    }

    private static async Task<int> RunFixGetAwaiterGetResultCommand(string[] args) {
        if (args.Length == 0 || args.Contains("--help", StringComparer.Ordinal)) {
            Console.WriteLine("用法: jcc-audit fix-getawaiter-getresult <项目根目录> [--dry-run] [--cascade <slnx>]");
            Console.WriteLine();
            Console.WriteLine("修复: 同步 private 方法中 .GetAwaiter().GetResult() → async + await ... .ConfigureAwait(false)");
            Console.WriteLine("跳过: public/lambda/Main/属性getter/分析器代码");
            Console.WriteLine("--cascade <slnx>: 修复后递归处理级联错误（CS4014/CS0029/CS1503），编译错误驱动");
            return 0;
        }

        var targetPath = args[0];
        var dryRun = args.Contains("--dry-run", StringComparer.Ordinal);
        var cascadeIdx = Array.IndexOf(args, "--cascade");
        var slnPath = cascadeIdx >= 0 && cascadeIdx + 1 < args.Length ? args[cascadeIdx + 1] : null;

        Console.WriteLine("=== JccAuditCli fix-getawaiter-getresult ===");
        Console.WriteLine($"项目根目录: {Path.GetFullPath(targetPath)}");
        Console.WriteLine($"模式: {(dryRun ? "预览 (DryRun)" : "实际写入")}");
        if (slnPath is not null)
            Console.WriteLine($"级联修复: 启用（{slnPath}）");

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(30));

        try {
            var (fixedFiles, fixedIssues, skipped) = await GetAwaiterFixer.FixAllAsync(targetPath, dryRun, cts.Token).ConfigureAwait(false);

            Console.WriteLine();
            Console.WriteLine("=== fix-getawaiter-getresult 报告 ===");
            Console.WriteLine($"修复文件: {fixedFiles}");
            Console.WriteLine($"修复问题: {fixedIssues}");
            Console.WriteLine($"跳过文件: {skipped}");

            if (slnPath is not null && !dryRun && fixedIssues > 0) {
                Console.WriteLine();
                Console.WriteLine("=== 级联修复（编译错误驱动递归）===");
                var (iterations, cascadeFixed) = await CascadeAsyncFixer.FixCascadeAsync(
                    slnPath, maxIterations: 20, dryRun: false, cts.Token).ConfigureAwait(false);
                Console.WriteLine($"级联迭代: {iterations}");
                Console.WriteLine($"级联修复: {cascadeFixed} 处");
            }

            return fixedIssues > 0 ? (dryRun ? 3 : 0) : 0;
        } catch (OperationCanceledException) {
            Console.Error.WriteLine("扫描超时（30 分钟限制）。");
            return 2;
        }
    }

    /// <summary>
    /// cascade-fix 模式：单独运行级联修复器，处理初始修复后的编译错误。
    /// 用法: jcc-audit cascade-fix &lt;slnx&gt; [--dry-run] [--max-iterations N]
    /// </summary>
    private static async Task<int> RunCascadeFixCommand(string[] args) {
        if (args.Length == 0 || args.Contains("--help", StringComparer.Ordinal)) {
            Console.WriteLine("用法: jcc-audit cascade-fix <slnx> [--dry-run] [--max-iterations N]");
            Console.WriteLine();
            Console.WriteLine("单独运行级联修复器，处理编译错误（CS4014/CS0029/CS1503/CS1929/CS0019）");
            Console.WriteLine("状态机驱动：dotnet build → 解析错误 → 修复 → 重新编译，直到无错误");
            return 0;
        }

        var slnPath = args[0];
        var dryRun = args.Contains("--dry-run", StringComparer.Ordinal);
        var maxIterIdx = Array.IndexOf(args, "--max-iterations");
        var maxIterations = maxIterIdx >= 0 && maxIterIdx + 1 < args.Length
            ? int.Parse(args[maxIterIdx + 1])
            : 20;

        Console.WriteLine("=== JccAuditCli cascade-fix ===");
        Console.WriteLine($"解决方案: {Path.GetFullPath(slnPath)}");
        Console.WriteLine($"模式: {(dryRun ? "预览 (DryRun)" : "实际写入")}");

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(30));

        try {
            var (iterations, cascadeFixed) = await CascadeAsyncFixer.FixCascadeAsync(
                slnPath, maxIterations, dryRun, cts.Token).ConfigureAwait(false);
            Console.WriteLine();
            Console.WriteLine("=== cascade-fix 报告 ===");
            Console.WriteLine($"迭代次数: {iterations}");
            Console.WriteLine($"修复总数: {cascadeFixed} 处");
            return cascadeFixed > 0 ? (dryRun ? 3 : 0) : 0;
        } catch (OperationCanceledException) {
            Console.Error.WriteLine("超时（30 分钟限制）。");
            return 2;
        }
    }
    private static async Task<int> RunFixJcc3016Command(string[] args) {
        if (args.Length == 0 || args.Contains("--help", StringComparer.Ordinal)) {
            Console.WriteLine("用法: jcc-audit fix-jcc3016 <slnx|sln|csproj> [--dry-run]");
            Console.WriteLine();
            Console.WriteLine("检测: SyncMethodAsyncCallDetector.IsViolation()（与分析器共享同一套逻辑）");
            Console.WriteLine("修复: invocation → invocation.GetAwaiter().GetResult()");
            Console.WriteLine("跳过: artifacts/obj/bin 生成代码 / bcl_bridge / 分析器代码");
            return 0;
        }

        var targetPath = args[0];
        var dryRun = args.Contains("--dry-run", StringComparer.Ordinal);

        Console.WriteLine("=== JccAuditCli fix-jcc3016 ===");
        Console.WriteLine($"解决方案: {Path.GetFullPath(targetPath)}");
        Console.WriteLine($"模式: {(dryRun ? "预览 (DryRun)" : "实际写入")}");

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));

        try {
            var (fixedFiles, fixedIssues, skipped) = await AnalyzerDrivenFixer.FixAsync(targetPath, dryRun, cts.Token).ConfigureAwait(false);

            Console.WriteLine();
            Console.WriteLine("=== fix-jcc3016 报告 ===");
            Console.WriteLine($"修复文件: {fixedFiles}");
            Console.WriteLine($"修复问题: {fixedIssues}");
            Console.WriteLine($"跳过: {skipped}");

            return fixedIssues > 0 ? (dryRun ? 3 : 0) : 0;
        } catch (OperationCanceledException) {
            Console.Error.WriteLine("扫描超时（10 分钟限制）。");
            return 2;
        }
    }

    /// <summary>
    /// analyze-getawaiter 模式：AST 遍历所有方法，检测 .GetAwaiter().GetResult() 在同步/异步方法中的分布
    /// </summary>
    private static async Task<int> RunAnalyzeGetAwaiterCommand(string[] args) {
        if (args.Length == 0 || args.Contains("--help", StringComparer.Ordinal)) {
            Console.WriteLine("用法: jcc-audit analyze-getawaiter <项目根目录>");
            Console.WriteLine();
            Console.WriteLine("检测: AST 遍历所有方法节点，找 .GetAwaiter().GetResult() 调用");
            Console.WriteLine("分类: 同步方法（非 async）vs 异步方法（async）");
            Console.WriteLine("输出: 报告（只检测，不修改）");
            return 0;
        }

        var rootPath = args[0];

        Console.WriteLine("=== JccAuditCli analyze-getawaiter ===");
        Console.WriteLine($"项目根目录: {Path.GetFullPath(rootPath)}");

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));

        try {
            var syncCount = await GetAwaiterAnalyzer.AnalyzeAsync(rootPath, cts.Token).ConfigureAwait(false);

            Console.WriteLine();
            if (syncCount > 0) {
                Console.WriteLine($"发现 {syncCount} 处同步方法中的 .GetAwaiter().GetResult()，可考虑改为异步 + await。");
            } else {
                Console.WriteLine("未发现同步方法中的 .GetAwaiter().GetResult()。");
            }

            return syncCount > 0 ? 1 : 0;
        } catch (OperationCanceledException) {
            Console.Error.WriteLine("扫描超时（5 分钟限制）。");
            return 2;
        }
    }

    /// <summary>
    /// </summary>
    private static async Task<int> RunFixDisposableDirectionCommand(string[] args, FixDirection direction) {
        var dirName = direction == FixDirection.Async ? "async" : "sync";
        var dirDesc = direction == FixDirection.Async
            ? "IDisposable → IAsyncDisposable (异步化)"
            : "IAsyncDisposable → IDisposable (同步化)";

        if (args.Length == 0 || args.Contains("--help", StringComparer.Ordinal)) {
            Console.WriteLine($"用法: jcc-audit fix-disposable-{dirName} <slnx> --target-type <TypeName> [--dry-run]");
            Console.WriteLine();
            Console.WriteLine($"方向: {dirDesc}");
            Console.WriteLine("改动:");
            Console.WriteLine("  1. 接口声明: IDisposable ↔ IAsyncDisposable");
            Console.WriteLine("  2. 方法签名: void Dispose() ↔ ValueTask DisposeAsync()");
            Console.WriteLine("  3. 调用点:   Dispose() ↔ await DisposeAsync()");
            Console.WriteLine("  4. using 声明: using var ↔ await using var");
            return 0;
        }

        var targetPath = args[0];
        var targetType = GetArgValue(args, "--target-type") ?? string.Empty;
        var dryRun = args.Contains("--dry-run", StringComparer.Ordinal);

        if (string.IsNullOrEmpty(targetType)) {
            Console.Error.WriteLine("必须指定 --target-type <TypeName>。");
            return 1;
        }

        Console.WriteLine($"=== JccAuditCli fix-disposable-{dirName} ===");
        Console.WriteLine($"解决方案: {Path.GetFullPath(targetPath)}");
        Console.WriteLine($"目标类型: {targetType}");
        Console.WriteLine($"方向:     {dirDesc}");
        Console.WriteLine($"模式:     {(dryRun ? "预览 (DryRun)" : "实际写入")}");

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));

        try {
            var report = await DisposableDirectionFixer.FixAsync(targetPath, targetType, direction, dryRun, cts.Token).ConfigureAwait(false);
            report.PrintSummary();

            return report.TotalChanges > 0 ? (dryRun ? 3 : 0) : 0;
        } catch (OperationCanceledException) {
            Console.Error.WriteLine("扫描超时（10 分钟限制）。");
            return 2;
        }
    }

    /// <summary>
    /// sync-async 模式：把 async/await 同步化（移除 async 关键字，改方法签名，await → .GetAwaiter().GetResult()）
    /// </summary>
    private static async Task<int> RunSyncAsyncCommand(string[] args) {
        if (args.Length == 0 || args.Contains("--help", StringComparer.Ordinal) || args.Contains("-h", StringComparer.Ordinal)) {
            Console.WriteLine("用法: jcc-audit sync-async <slnx> [--dry-run] [--exclude <dir>]... [--include <dir>]...");
            Console.WriteLine();
            Console.WriteLine("同步化转换:");
            Console.WriteLine("  1. async Task<T> Method() → T Method()（移除 async，改返回类型）");
            Console.WriteLine("  2. async Task Method() → void Method()");
            Console.WriteLine("  3. await expr → expr.GetAwaiter().GetResult()（外部库方法）");
            Console.WriteLine("  4. await Method() → Method()（源码内 async 方法，签名已改）");
            Console.WriteLine("  5. return Task.CompletedTask → 移除");
            Console.WriteLine("  6. return Task.FromResult(x) → return x");
            Console.WriteLine("  7. await using → using, await foreach → foreach");
            Console.WriteLine();
            Console.WriteLine("选项:");
            Console.WriteLine("  --dry-run              仅预览，不实际写入文件");
            Console.WriteLine("  --exclude <dir>        排除目录（可多次指定，如 --exclude app/gui）");
            Console.WriteLine("  --include <dir>        只处理指定目录（可多次指定，如 --include lib/abstractions）");
            return 0;
        }

        var targetPath = args[0];
        var dryRun = args.Contains("--dry-run", StringComparer.Ordinal);
        var excludes = new List<string>();
        var includes = new List<string>();
        for (var i = 0; i < args.Length - 1; i++) {
            if (args[i] == "--exclude" && i + 1 < args.Length)
                excludes.Add(args[i + 1]);
            if (args[i] == "--include" && i + 1 < args.Length)
                includes.Add(args[i + 1]);
        }

        if (string.IsNullOrEmpty(targetPath)) {
            Console.Error.WriteLine("必须指定解决方案路径。");
            return 1;
        }

        Console.WriteLine("=== JccAuditCli sync-async ===");
        Console.WriteLine($"解决方案: {Path.GetFullPath(targetPath)}");
        Console.WriteLine($"模式: {(dryRun ? "预览 (DryRun)" : "实际写入")}");
        if (excludes.Count > 0)
            Console.WriteLine($"排除: {string.Join(", ", excludes)}");
        if (includes.Count > 0)
            Console.WriteLine($"只处理: {string.Join(", ", includes)}");

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(15));

        try {
            var (fixedFiles, fixedMethods, fixedAwaits, skipped) = await SyncAsyncFixer.SyncAllAsync(targetPath, dryRun, excludes.ToArray(), includes.ToArray(), cts.Token).ConfigureAwait(false);

            Console.WriteLine();
            Console.WriteLine("=== sync-async 报告 ===");
            Console.WriteLine($"修复文件: {fixedFiles}");
            Console.WriteLine($"修复方法: {fixedMethods}");
            Console.WriteLine($"修复 await: {fixedAwaits}");
            Console.WriteLine($"跳过文件: {skipped}");

            return fixedMethods + fixedAwaits > 0 ? (dryRun ? 3 : 0) : 0;
        } catch (OperationCanceledException) {
            Console.Error.WriteLine("扫描超时（15 分钟限制）。");
            return 2;
        }
    }

    private static void PrintStripBomTextReport(BomStripReport report) {
        Console.WriteLine();
        Console.WriteLine("=== BOM 移除报告 ===");
        Console.WriteLine($"目录: {report.RootPath}");
        Console.WriteLine($"时间: {report.Timestamp:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"--- 遍历 ---");
        Console.WriteLine($"发现 .cs 文件: {report.TotalCsFiles}");
        Console.WriteLine($"跳过文件: {report.SkippedFiles}");
        Console.WriteLine($"实际扫描: {report.ScannedFiles}");
        Console.WriteLine($"跳过规则:");
        Console.WriteLine($"  排除目录: {string.Join(", ", BomStripper.ExcludedDirectories)}");
        Console.WriteLine($"  排除文件: {string.Join(", ", BomStripper.ExcludedFilePatterns)}");
        if (report.SkipTests) {
            Console.WriteLine($"  排除测试: {string.Join(", ", BomStripper.ExcludedTestMarkers)}");
        }
        Console.WriteLine($"--- 检测 ---");
        Console.WriteLine($"含 BOM 文件: {report.WithBomCount}");
        Console.WriteLine($"--- 处理 ---");
        Console.WriteLine($"已移除: {report.StrippedCount}{(report.DryRun ? " (DryRun，未实际写入)" : "")}");
        Console.WriteLine();

        if (report.Files.Count == 0) {
            Console.WriteLine("未发现含 UTF-8 BOM 的文件，编码格式统一。");
            return;
        }

        foreach (var file in report.Files) {
            Console.WriteLine($"  {file.FilePath}");
        }
    }

    private static void PrintStripBomUsage() {
        Console.WriteLine("jcc-audit strip-bom - 移除 .cs 文件 UTF-8 BOM");
        Console.WriteLine();
        Console.WriteLine("用法: jcc-audit strip-bom <directory> [选项]");
        Console.WriteLine();
        Console.WriteLine("参数:");
        Console.WriteLine("  <directory>            扫描目录路径");
        Console.WriteLine();
        Console.WriteLine("选项:");
        Console.WriteLine("  --dry-run              仅预览，不实际写入文件");
        Console.WriteLine("  --output <file>        输出 JSON 报告到文件");
        Console.WriteLine("  --format <json|text>   输出格式（默认 text）");
        Console.WriteLine("  --skip-tests           跳过测试/基准/Mock 项目");
        Console.WriteLine();
        Console.WriteLine("示例:");
        Console.WriteLine("  jcc-audit strip-bom .");
        Console.WriteLine("  jcc-audit strip-bom . --dry-run");
        Console.WriteLine("  jcc-audit strip-bom ./src --skip-tests");
        Console.WriteLine("  jcc-audit strip-bom . --format json --output bom-report.json");
    }

    private static void PrintLayerAuditTextReport(LayerAuditReport report) {
        Console.WriteLine();
        Console.WriteLine("=== 层依赖审计报告 ===");
        Console.WriteLine($"目标: {report.TargetPath}");
        Console.WriteLine($"时间: {report.Timestamp:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"总项目: {report.TotalProjects}");
        Console.WriteLine($"违规数: {report.TotalViolations}（{report.ErrorCount} Error）");
        Console.WriteLine();

        if (report.Violations.Count == 0) {
            Console.WriteLine("未发现层依赖违规，七层架构隔离良好。");
            return;
        }

        foreach (var v in report.Violations) {
            Console.WriteLine($"[{v.RuleId}] {v.Severity}: {v.Message}");
        }
    }

    private static void PrintTopFilesTextReport(FileLineReport report) {
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

        if (report.Files.Count == 0) {
            Console.WriteLine($"未发现超过 {report.Threshold} 行的文件，代码组织良好。");
            return;
        }

        var maxLineDigits = report.Files[0].LineCount.ToString().Length;

        for (var i = 0; i < report.Files.Count; i++) {
            var file = report.Files[i];
            var severity = file.LineCount >= 2000 ? "!!" : file.LineCount >= 1000 ? "! " : "  ";
            Console.WriteLine($"  {severity} {i + 1,2}. {file.LineCount.ToString().PadLeft(maxLineDigits)} 行 - {file.FilePath}");
        }

        Console.WriteLine();
        Console.WriteLine("  !! = 超过2000行（紧急拆分）  ! = 超过1000行（建议拆分）");
    }

    private static void PrintTopFilesUsage() {
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

    private static void PrintCtorAuditUsage() {
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

    private static void PrintCtorTextReport(ConstructorParamReport report) {
        Console.WriteLine();
        Console.WriteLine("=== 构造函数参数审计报告 ===");
        Console.WriteLine($"目标: {report.TargetPath}");
        Console.WriteLine($"时间: {report.Timestamp:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"阈值: {report.Threshold}");
        Console.WriteLine($"胖构造函数总数: {report.TotalFatCtors}");
        Console.WriteLine();

        if (report.Constructors.Count == 0) {
            Console.WriteLine("未发现胖构造函数，代码结构良好。");
            return;
        }

        // 按参数数量降序排列，分组展示
        foreach (var ctor in report.Constructors) {
            var severity = ctor.ParameterCount >= 12 ? "ERROR" : "WARN";
            Console.WriteLine($"  [{severity}] {ctor.ParameterCount} 个参数 - {ctor.ClassName}");
            Console.WriteLine($"    文件: {ctor.FilePath}:{ctor.LineNumber}");
            Console.WriteLine($"    签名: {ctor.ConstructorSignature}");
            Console.WriteLine($"    参数: {string.Join(", ", ctor.ParameterTypes)}");
            Console.WriteLine($"    建议: 考虑将相关参数聚合为中间件上下文，减少构造函数注入");
            Console.WriteLine();
        }
    }

    private static void PrintReplaceUsage() {
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

    private static void PrintTextReport(AuditReport report) {
        Console.WriteLine();
        Console.WriteLine($"=== 审计报告 ===");
        Console.WriteLine($"目标: {report.TargetPath}");
        Console.WriteLine($"时间: {report.Timestamp:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"项目数: {report.TotalProjects}");
        Console.WriteLine($"诊断总数: {report.TotalDiagnostics}");
        Console.WriteLine();

        foreach (var project in report.Projects) {
            if (project.TotalDiagnostics == 0)
                continue;

            Console.WriteLine($"[{project.ProjectName}] {project.TotalDiagnostics} 条诊断 (W:{project.WarningCount} E:{project.ErrorCount} I:{project.InfoCount})");

            foreach (var diag in project.Diagnostics) {
                Console.WriteLine($"  {diag.RuleId} [{diag.Severity}] {diag.FilePath}:{diag.Line}:{diag.Column}");
                Console.WriteLine($"    {diag.Message}");
            }

            Console.WriteLine();
        }
    }

    private static string? GetArgValue(string[] args, string key) {
        for (var i = 0; i < args.Length - 1; i++) {
            if (args[i] == key)
                return args[i + 1];
        }

        return null;
    }

    private static string FindProjectRoot(string targetPath) {
        var dir = Path.GetDirectoryName(Path.GetFullPath(targetPath));

        while (!string.IsNullOrEmpty(dir)) {
            if (File.Exists(Path.Combine(dir, "JoinCode.slnx")) ||
                File.Exists(Path.Combine(dir, "JoinCode.sln"))) {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        return Path.GetDirectoryName(Path.GetFullPath(targetPath))!;
    }
}