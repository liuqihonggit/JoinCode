namespace CtorScanner;

internal static class Program
{
    private static readonly string[] ScanDirs = ["lib", "kit", "server", "llm", "app"];

    private static readonly string[] DisposableKeywords =
        ["Stream", "Reader", "Writer", "Process", "Thread", "Timer",
         "Semaphore", "Mutex", "Socket", "Handle", "Archive",
         "CancellationTokenSource", "RegistryKey", "WaitHandle",
         "FileSystemWatcher", "Pipe", "Channel", "Zip", "GZip", "Deflate", "Brotli"];

    private static readonly string[] FactoryMethodNames =
        ["Create", "Open", "CreateAsync", "OpenAsync", "From", "FromAsync", "Build", "BuildAsync"];

    private static readonly string[] IoMethodPrefixes =
        ["File.Open", "File.Create", "File.OpenRead", "File.OpenWrite", "File.OpenText",
         "File.ReadAll", "File.Append", "File.Write", "Directory.Create", "Directory.Open"];

    public static int Main(string[] args)
    {
        var repoRoot = FindRepoRoot();
        if (repoRoot == null)
        {
            Console.Error.WriteLine("Cannot find repo root (no .git found)");
            return 1;
        }

        Console.WriteLine($"Repo root: {repoRoot}");
        Console.WriteLine("Scanning for constructors...");

        var csFiles = FindAllCsFiles(repoRoot);
        Console.WriteLine($"Found {csFiles.Count} .cs files");

        var totalConstructors = 0;
        var riskConstructors = new ConcurrentBag<ConstructorRisk>();
        var alreadyRefactored = 0;
        var riskTypeCounts = new ConcurrentDictionary<string, int>();

        Parallel.ForEach(csFiles, file =>
        {
            try
            {
                var source = File.ReadAllText(file);
                var tree = CSharpSyntaxTree.ParseText(SourceText.From(source, Encoding.UTF8), path: file);
                var root = tree.GetCompilationUnitRoot();

                var constructors = root.DescendantNodes().OfType<ConstructorDeclarationSyntax>();
                foreach (var ctor in constructors)
                {
                    Interlocked.Increment(ref totalConstructors);

                    var parent = ctor.Parent as TypeDeclarationSyntax;
                    var className = parent?.Identifier.Text ?? "Unknown";
                    var hasFactory = parent != null && HasStaticFactoryMethod(parent);
                    var (risks, details) = AnalyzeConstructor(ctor);

                    if (risks.Count > 0)
                    {
                        var relativePath = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
                        var line = ctor.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                        var accessibility = GetAccessibility(ctor);
                        var bodyPreview = ctor.Body?.ToString() ?? "";
                        if (bodyPreview.Length > 500)
                            bodyPreview = bodyPreview[..500] + " ...";
                        var paramList = ctor.ParameterList.ToString();

                        riskConstructors.Add(new ConstructorRisk
                        {
                            File = relativePath,
                            Line = line,
                            ClassName = className,
                            Accessibility = accessibility,
                            HasStaticFactory = hasFactory,
                            Risks = risks,
                            Details = details,
                            BodyPreview = bodyPreview,
                            ParameterList = paramList
                        });

                        foreach (var r in risks)
                            riskTypeCounts.AddOrUpdate(r, 1, (_, v) => v + 1);
                    }

                    if (hasFactory)
                        Interlocked.Increment(ref alreadyRefactored);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error parsing {file}: {ex.Message}");
            }
        });

        var sortedResults = riskConstructors.OrderBy(r => r.File).ThenBy(r => r.Line).ToList();

        var report = new ScanReport
        {
            Summary = new ScanSummary
            {
                TotalFiles = csFiles.Count,
                TotalConstructors = totalConstructors,
                RiskConstructors = sortedResults.Count,
                ByRiskType = new Dictionary<string, int>(riskTypeCounts),
                AlreadyRefactored = alreadyRefactored
            },
            Results = sortedResults
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        var json = JsonSerializer.Serialize(report, options);
        var outputPath = Path.Combine(repoRoot, "tool", "ctor_scanner", "ctor_scan_report.json");
        File.WriteAllText(outputPath, json);

        Console.WriteLine();
        Console.WriteLine("=== Scan Summary ===");
        Console.WriteLine($"Total files: {report.Summary.TotalFiles}");
        Console.WriteLine($"Total constructors: {report.Summary.TotalConstructors}");
        Console.WriteLine($"Risk constructors: {report.Summary.RiskConstructors}");
        Console.WriteLine($"Already refactored (has factory method): {report.Summary.AlreadyRefactored}");
        Console.WriteLine();
        Console.WriteLine("By risk type:");
        foreach (var (type, count) in report.Summary.ByRiskType.OrderByDescending(x => x.Value))
            Console.WriteLine($"  {type}: {count}");

        Console.WriteLine();
        Console.WriteLine($"Report saved to: {outputPath}");

        Console.WriteLine();
        Console.WriteLine("=== Top 30 Risk Constructors ===");
        foreach (var r in sortedResults.Take(30))
        {
            Console.WriteLine($"  {r.File}:{r.Line} {r.Accessibility} {r.ClassName}({r.ParameterList}) [{string.Join(", ", r.Risks)}]");
        }
        if (sortedResults.Count > 30)
            Console.WriteLine($"  ... and {sortedResults.Count - 30} more");

        return 0;
    }

    private static string? FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, ".git")) || File.Exists(Path.Combine(dir, ".git")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    private static List<string> FindAllCsFiles(string repoRoot)
    {
        var files = new List<string>();
        var sep = Path.DirectorySeparatorChar;
        foreach (var dir in ScanDirs)
        {
            var fullPath = Path.Combine(repoRoot, dir);
            if (!Directory.Exists(fullPath)) continue;
            files.AddRange(Directory.EnumerateFiles(fullPath, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith(".g.cs") &&
                            !f.EndsWith(".Designer.cs") &&
                            !f.EndsWith(".generated.cs") &&
                            !f.Contains($"{sep}bin{sep}") &&
                            !f.Contains($"{sep}obj{sep}") &&
                            !f.Contains($"{sep}.xxx{sep}")));
        }
        return files;
    }

    private static (List<string> Risks, List<string> Details) AnalyzeConstructor(ConstructorDeclarationSyntax ctor)
    {
        var body = ctor.Body;
        if (body == null) return ([], []);

        var risks = new HashSet<string>();
        var details = new List<string>();

        foreach (var node in body.DescendantNodes())
        {
            var line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

            if (node is ObjectCreationExpressionSyntax oc)
            {
                var typeName = oc.Type.ToString();
                if (IsDisposableAllocation(typeName))
                {
                    risks.Add("IDisposableAllocation");
                    details.Add($"new {typeName} at line {line}");
                }
                if (typeName.Contains("CancellationTokenSource"))
                {
                    risks.Add("CancellationTokenSourceCreation");
                    details.Add($"new {typeName} at line {line}");
                }
                if (typeName == "Thread")
                {
                    risks.Add("ThreadCreation");
                    details.Add($"new Thread at line {line}");
                }
            }

            if (node is AssignmentExpressionSyntax ae)
            {
                if (ae.Left is IdentifierNameSyntax ins && ins.Identifier.Text == "_")
                {
                    risks.Add("FireAndForget");
                    details.Add($"_ = {ae.Right} at line {line}");
                }
                if (ae.Kind() == SyntaxKind.AddAssignmentExpression)
                {
                    risks.Add("EventSubscription");
                    details.Add($"+= at line {line}");
                }
            }

            if (node is InvocationExpressionSyntax inv)
            {
                var exprText = inv.Expression.ToString();
                if (exprText.Contains("Task.Run") || exprText.Contains("Task.Factory.StartNew"))
                {
                    risks.Add("TaskRun");
                    details.Add($"{exprText} at line {line}");
                }
                if (exprText.Contains("GCHandle.Alloc"))
                {
                    risks.Add("GCHandleAlloc");
                    details.Add($"GCHandle.Alloc at line {line}");
                }
                if (exprText.Contains("Process.Start"))
                {
                    risks.Add("ProcessStart");
                    details.Add($"Process.Start at line {line}");
                }
                foreach (var prefix in IoMethodPrefixes)
                {
                    if (exprText.StartsWith(prefix))
                    {
                        risks.Add("IOOperation");
                        details.Add($"{exprText} at line {line}");
                        break;
                    }
                }
                if (inv.Expression is MemberAccessExpressionSyntax ma)
                {
                    if (ma.Name.Identifier.Text == "Wait")
                    {
                        risks.Add("BlockingAsync_Wait");
                        details.Add($".Wait() at line {line}");
                    }
                    if (ma.Name.Identifier.Text == "GetResult")
                    {
                        risks.Add("BlockingAsync_GetResult");
                        details.Add($".GetResult() at line {line}");
                    }
                    if (ma.Name.Identifier.Text == "Start")
                    {
                        var receiver = ma.Expression.ToString();
                        if (receiver.Contains("Thread", StringComparison.Ordinal) ||
                            receiver.Contains("thread", StringComparison.OrdinalIgnoreCase))
                        {
                            risks.Add("ThreadStart");
                            details.Add($".Start() at line {line}");
                        }
                    }
                }
            }

            if (node is MemberAccessExpressionSyntax memberAccess && memberAccess.Name.Identifier.Text == "Result")
            {
                risks.Add("BlockingAsync_Result");
                details.Add($".Result at line {line}");
            }

            if (node is LockStatementSyntax)
            {
                risks.Add("LockStatement");
                details.Add($"lock at line {line}");
            }
        }

        return (risks.ToList(), details);
    }

    private static bool IsDisposableAllocation(string typeName)
    {
        foreach (var keyword in DisposableKeywords)
            if (typeName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static bool HasStaticFactoryMethod(TypeDeclarationSyntax typeDecl)
    {
        foreach (var method in typeDecl.Members.OfType<MethodDeclarationSyntax>())
        {
            if (method.Modifiers.Any(SyntaxKind.StaticKeyword) &&
                Array.IndexOf(FactoryMethodNames, method.Identifier.Text) >= 0)
            {
                return true;
            }
        }
        return false;
    }

    private static string GetAccessibility(ConstructorDeclarationSyntax ctor)
    {
        foreach (var modifier in ctor.Modifiers)
        {
            if (modifier.IsKind(SyntaxKind.PublicKeyword)) return "public";
            if (modifier.IsKind(SyntaxKind.InternalKeyword)) return "internal";
            if (modifier.IsKind(SyntaxKind.PrivateKeyword)) return "private";
            if (modifier.IsKind(SyntaxKind.ProtectedKeyword)) return "protected";
        }
        return "private";
    }
}

public sealed class ScanReport
{
    public required ScanSummary Summary { get; init; }
    public required List<ConstructorRisk> Results { get; init; }
}

public sealed class ScanSummary
{
    public required int TotalFiles { get; init; }
    public required int TotalConstructors { get; init; }
    public required int RiskConstructors { get; init; }
    public required Dictionary<string, int> ByRiskType { get; init; }
    public required int AlreadyRefactored { get; init; }
}

public sealed class ConstructorRisk
{
    public required string File { get; init; }
    public required int Line { get; init; }
    public required string ClassName { get; init; }
    public required string Accessibility { get; init; }
    public required bool HasStaticFactory { get; init; }
    public required List<string> Risks { get; init; }
    public required List<string> Details { get; init; }
    public required string BodyPreview { get; init; }
    public required string ParameterList { get; init; }
}
