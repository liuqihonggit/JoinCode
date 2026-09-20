namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC9001/JCC9002/JCC9006: 文件IO 抽象绕过检测。
/// 多描述符规则 — 共享 AnalyzeDirectFileIO 逻辑(File/Directory 调用 + FileStream 构造 + FileShare 参数)。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "CodeOrganization",
    Id = "JCC9001",
    Title = "文件IO: 直接调用 System.IO.File/Directory 应使用 IFileSystem 抽象",
    Description = "直接调用 '{0}' 绕过了 IFileSystem 抽象层，导致测试无法实现0磁盘读写。必须通过构造函数注入 IFileSystem（禁止 new PhysicalFileSystem()），使用对应方法替代。",
    Category = "FileSystemAbstraction",
    Severity = DiagnosticSeverity.Error,
    IsEnabledByDefault = true,
    HelpLinkUri = "所有文件读写必须通过 IFileSystem 接口，生产环境使用 PhysicalFileSystem，测试环境使用 InMemoryFileSystem." +
    "映射关系: File.Exists → fs.FileExists, File.ReadAllText/Async → fs.ReadAllText/Async, " +
    "File.WriteAllText/Async → fs.WriteAllText/Async, File.ReadAllBytes/Async → fs.ReadAllBytes/Async, " +
    "File.WriteAllBytes/Async → fs.WriteAllBytes/Async, File.AppendAllText/Async → fs.AppendAllText/Async, " +
    "File.Delete → fs.DeleteFile, File.Move → fs.MoveFile, File.Copy → fs.CopyFile, " +
    "File.OpenRead → fs.OpenRead, File.Open → fs.Open, new FileStream → fs.CreateStream, " +
    "Directory.Exists → fs.DirectoryExists, Directory.CreateDirectory → fs.CreateDirectory, " +
    "Directory.Delete → fs.DeleteDirectory, Directory.GetFiles → fs.GetFiles, " +
    "Directory.GetDirectories → fs.GetDirectories, Directory.EnumerateFiles → fs.EnumerateFiles, " +
    "Directory.EnumerateDirectories → fs.EnumerateDirectories, Directory.Move → fs.MoveDirectory, " +
    "Directory.GetCurrentDirectory → fs.GetCurrentDirectory, Directory.SetCurrentDirectory → fs.SetCurrentDirectory.")]
[AnalyzerRule(
    AnalyzerId = "CodeOrganization",
    Id = "JCC9002",
    Title = "文件IO: 直接 new FileStream 应使用 IFileSystem.CreateStream 抽象",
    Description = "直接构造 FileStream 绕过了 IFileSystem 抽象层，导致测试无法实现0磁盘读写。必须通过构造函数注入 IFileSystem（禁止 new PhysicalFileSystem()），使用 fs.CreateStream(path, mode, access, share) 替代。",
    Category = "FileSystemAbstraction",
    Severity = DiagnosticSeverity.Error,
    IsEnabledByDefault = true,
    HelpLinkUri = "FileStream 构造函数直接访问磁盘，测试中无法替换为内存实现.使用 IFileSystem.CreateStream 替代.")]
[AnalyzerRule(
    AnalyzerId = "CodeOrganization",
    Id = "JCC9006",
    Title = "文件IO: FileStream 必须使用 FileShare.ReadWrite 避免并发读写冲突",
    Description = "FileStream 的 FileShare 参数为 '{0}'，应使用 FileShare.ReadWrite 避免并发读写冲突。",
    Category = "FileSystemAbstraction",
    Severity = DiagnosticSeverity.Error,
    IsEnabledByDefault = true,
    HelpLinkUri = "所有 FileStream 必须显式指定 FileShare.ReadWrite.不指定时默认 FileShare.None，会导致并发读取时写入失败（UnauthorizedAccessException）." +
    "正确: new FileStream(path, mode, access, FileShare.ReadWrite) 或 fs.CreateStream(path, mode, access, FileShare.ReadWrite).")]
public sealed class DirectFileIORule : IAnalyzerRule {
    private static readonly IReadOnlyDictionary<string, DiagnosticDescriptor> Map = RuleDescriptorFactory.CreateAll<DirectFileIORule>();
    public IReadOnlyList<DiagnosticDescriptor> Descriptors { get; } = Map.Values.ToList();

    private static readonly HashSet<string> FileMethods = new(StringComparer.Ordinal)
    {
        "Exists", "ReadAllText", "ReadAllTextAsync", "WriteAllText", "WriteAllTextAsync",
        "ReadAllLines", "ReadAllLinesAsync", "WriteAllLines", "WriteAllLinesAsync",
        "ReadAllBytes", "ReadAllBytesAsync", "WriteAllBytes", "WriteAllBytesAsync",
        "AppendAllText", "AppendAllTextAsync", "AppendAllLines", "AppendAllLinesAsync",
        "Delete", "Move", "Copy", "Open", "OpenRead", "OpenWrite", "Create",
        "GetLastWriteTime", "GetLastWriteTimeUtc", "SetLastWriteTimeUtc",
        "GetCreationTime", "GetAttributes",
    };

    private static readonly HashSet<string> DirectoryMethods = new(StringComparer.Ordinal)
    {
        "Exists", "CreateDirectory", "Delete", "Move",
        "GetFiles", "GetDirectories", "EnumerateFiles", "EnumerateDirectories",
        "GetCurrentDirectory", "SetCurrentDirectory",
        "GetLastWriteTimeUtc", "GetParent",
    };

    public void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(AnalyzeDirectFileIO, SyntaxKind.InvocationExpression, SyntaxKind.ObjectCreationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeDirectDirectoryProperty, SyntaxKind.SimpleMemberAccessExpression);
    }

    private static void AnalyzeDirectFileIO(SyntaxNodeAnalysisContext ctx) {
        if (ctx.CancellationToken.IsCancellationRequested) return;

        if (IsInFileSystemImplementation(ctx)) return;
        if (IsInDeprecatedOrGeneratedCode(ctx)) return;

        switch (ctx.Node) {
            case InvocationExpressionSyntax invocation:
            AnalyzeFileDirectoryInvocation(ctx, invocation);
            break;
            case ObjectCreationExpressionSyntax objectCreation:
            AnalyzeFileStreamCreation(ctx, objectCreation);
            break;
        }
    }

    private static void AnalyzeDirectDirectoryProperty(SyntaxNodeAnalysisContext ctx) {
        if (ctx.CancellationToken.IsCancellationRequested) return;
        if (IsInFileSystemImplementation(ctx)) return;
        if (IsInDeprecatedOrGeneratedCode(ctx)) return;

        if (ctx.Node is not MemberAccessExpressionSyntax memberAccess) return;

        var symbolInfo = ctx.SemanticModel.GetSymbolInfo(memberAccess);
        if (symbolInfo.Symbol is not IMethodSymbol method) return;

        var containingType = method.ContainingType;
        if (containingType is null) return;

        var typeName = containingType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

        if ((typeName is "File" or "System.IO.File") && FileMethods.Contains(method.Name)) {
            ctx.ReportDiagnostic(Diagnostic.Create(Map["JCC9001"], memberAccess.GetLocation(), $"File.{method.Name}"));
        } else if ((typeName is "Directory" or "System.IO.Directory") && DirectoryMethods.Contains(method.Name)) {
            ctx.ReportDiagnostic(Diagnostic.Create(Map["JCC9001"], memberAccess.GetLocation(), $"Directory.{method.Name}"));
        }
    }

    private static void AnalyzeFileDirectoryInvocation(SyntaxNodeAnalysisContext ctx, InvocationExpressionSyntax invocation) {
        var symbolInfo = ctx.SemanticModel.GetSymbolInfo(invocation);
        if (symbolInfo.Symbol is not IMethodSymbol method) return;

        var containingType = method.ContainingType;
        if (containingType is null) return;

        var typeName = containingType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

        if ((typeName is "File" or "System.IO.File") && FileMethods.Contains(method.Name)) {
            ctx.ReportDiagnostic(Diagnostic.Create(Map["JCC9001"], invocation.GetLocation(), $"File.{method.Name}"));
        } else if ((typeName is "Directory" or "System.IO.Directory") && DirectoryMethods.Contains(method.Name)) {
            ctx.ReportDiagnostic(Diagnostic.Create(Map["JCC9001"], invocation.GetLocation(), $"Directory.{method.Name}"));
        }
    }

    private static void AnalyzeFileStreamCreation(SyntaxNodeAnalysisContext ctx, ObjectCreationExpressionSyntax objectCreation) {
        var symbolInfo = ctx.SemanticModel.GetSymbolInfo(objectCreation);
        if (symbolInfo.Symbol is not IMethodSymbol ctor) return;

        var containingType = ctor.ContainingType;
        if (containingType is null) return;

        var typeName = containingType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        if (typeName is "FileStream" or "System.IO.FileStream") {
            ctx.ReportDiagnostic(Diagnostic.Create(Map["JCC9002"], objectCreation.GetLocation()));
            AnalyzeFileShareParameter(ctx, objectCreation);
        }
    }

    private static void AnalyzeFileShareParameter(SyntaxNodeAnalysisContext ctx, ObjectCreationExpressionSyntax objectCreation) {
        var argumentList = objectCreation.ArgumentList;
        if (argumentList is null) return;

        var arguments = argumentList.Arguments;
        if (arguments.Count == 0) return;

        ArgumentSyntax? shareArg = null;
        foreach (var arg in arguments) {
            if (arg.NameColon is { } nameColon && nameColon.Name.Identifier.ValueText == "share") {
                shareArg = arg;
                break;
            }
        }

        if (shareArg is null) {
            var positionalArgs = arguments.Where(a => a.NameColon is null).ToList();
            if (positionalArgs.Count >= 4) {
                shareArg = positionalArgs[3];
            }
        }

        if (shareArg is null) {
            ctx.ReportDiagnostic(Diagnostic.Create(Map["JCC9006"], objectCreation.GetLocation(), "未指定(默认None)"));
            return;
        }

        var constantValue = ctx.SemanticModel.GetConstantValue(shareArg.Expression);
        if (constantValue.HasValue && constantValue.Value is int shareValue) {
            if ((shareValue & 3) == 3) return;
            ctx.ReportDiagnostic(Diagnostic.Create(Map["JCC9006"], shareArg.GetLocation(), shareValue.ToString()));
            return;
        }

        var exprText = shareArg.Expression.ToString();
        if (exprText.Contains("ReadWrite")) return;
        ctx.ReportDiagnostic(Diagnostic.Create(Map["JCC9006"], shareArg.GetLocation(), exprText));
    }

    private static bool IsInFileSystemImplementation(SyntaxNodeAnalysisContext ctx) {
        var containingType = ctx.SemanticModel.GetEnclosingSymbol(ctx.Node.SpanStart)?.ContainingType;
        if (containingType is null) return false;

        var typeName = containingType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        return typeName.Contains("PhysicalFileSystem", StringComparison.Ordinal) ||
               typeName.Contains("InMemoryFileSystem", StringComparison.Ordinal) ||
               typeName.Contains("SimpleFileReader", StringComparison.Ordinal) ||
               typeName.Contains("FileReader", StringComparison.Ordinal) ||
               typeName.Contains("FileOperationService", StringComparison.Ordinal);
    }

    private static bool IsInDeprecatedOrGeneratedCode(SyntaxNodeAnalysisContext ctx) {
        var containingType = ctx.SemanticModel.GetEnclosingSymbol(ctx.Node.SpanStart)?.ContainingType;
        if (containingType is not null) {
            foreach (var attr in containingType.GetAttributes()) {
                if (attr.AttributeClass?.Name == "ObsoleteAttribute")
                    return true;
            }
        }

        var containingMethod = ctx.SemanticModel.GetEnclosingSymbol(ctx.Node.SpanStart) as IMethodSymbol;
        if (containingMethod is not null) {
            foreach (var attr in containingMethod.GetAttributes()) {
                if (attr.AttributeClass?.Name == "ObsoleteAttribute")
                    return true;
            }
        }

        return false;
    }
}
