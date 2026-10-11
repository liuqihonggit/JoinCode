namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC9003/JCC9004/JCC9005: 抽象层绕过检测(HttpClient/PhysicalFileSystem/FileSystemWatcher)。
/// 多描述符规则 — 共享 AnalyzeAbstractionBypass 逻辑。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "CodeOrganization",
    Id = "JCC9003",
    Title = "HTTP: 直接 new HttpClient() 应使用 IHttpClientProvider 抽象",
    Description = "直接构造 HttpClient 绕过了 IHttpClientProvider 抽象层，导致 JCC_HTTP_MODE=Mock 环境变量无法生效。必须通过构造函数注入 IHttpClientProvider，使用 provider.GetClient() 替代。DI 容器构建前使用 HttpClientProviderFactory.Create().GetClient()。",
    Category = "HttpAbstraction",
    Severity = DiagnosticSeverity.Info,
    IsEnabledByDefault = true,
    HelpLinkUri = "HttpClient 直接 new 导致: 1) 无法通过 JCC_HTTP_MODE=Mock 切换为模拟实现; 2) 连接池无法共享，Socket 耗尽风险; 3) 测试无法 Mock HTTP 请求." +
    "替代方案: 1) DI 容器构建后: 构造函数注入 IHttpClientProvider，调用 provider.GetClient(); " +
    "2) DI 容器构建前: 使用 HttpClientProviderFactory.Create().GetClient(); " +
    "3) 需要自定义 Handler 的场景(如 PipeHttpMessageHandler/SocketsHttpHandler): 允许自建，但需注释说明原因." +
    "例外: Transport 传输层(V1/V2/SSE)、LLM 层(QueryServiceBase/PipeQueryService)、ApiClient 等需要自定义 Handler 的场景.")]
[AnalyzerRule(
    AnalyzerId = "CodeOrganization",
    Id = "JCC9004",
    Title = "文件IO: 直接 new PhysicalFileSystem() 应使用 IFileSystem 注入或 FileSystemFactory",
    Description = "直接构造 PhysicalFileSystem 绕过了 IFileSystem 抽象层，导致 JCC_FILE_SYSTEM_MODE=InMemory 环境变量无法生效。必须通过构造函数注入 IFileSystem，或使用 FileSystemFactory.Create() 替代。",
    Category = "FileSystemAbstraction",
    Severity = DiagnosticSeverity.Info,
    IsEnabledByDefault = true,
    HelpLinkUri = "PhysicalFileSystem 直接 new 导致: 1) 无法通过 JCC_FILE_SYSTEM_MODE=InMemory 切换为内存实现; 2) 测试无法实现0磁盘读写." +
    "替代方案: 1) DI 容器构建后: 构造函数注入 IFileSystem; " +
    "2) DI 容器构建前: 使用 FileSystemFactory.Create(); " +
    "3) DI 注册处: 已通过环境变量自动切换，无需手动 new.")]
[AnalyzerRule(
    AnalyzerId = "CodeOrganization",
    Id = "JCC9005",
    Title = "文件IO: 直接 new FileSystemWatcher() 绕过 IFileSystem 抽象",
    Description = "直接构造 FileSystemWatcher 绕过了 IFileSystem 抽象层，在 InMemory 模式下无法触发变更通知。应使用 IFileSystem.Watch() 方法获取 IFileSystemWatcher。",
    Category = "FileSystemAbstraction",
    Severity = DiagnosticSeverity.Error,
    IsEnabledByDefault = true,
    HelpLinkUri = "FileSystemWatcher 直接 new 导致: 1) InMemory 模式下无法触发变更通知; 2) 测试无法模拟文件变更事件. 替代方案: 使用 IFileSystem.Watch(path, filter) 获取 IFileSystemWatcher, 已支持 PhysicalFileSystem 和 InMemoryFileSystem 两种实现.")]
public sealed class AbstractionBypassRule : IAnalyzerRule {
    private static readonly IReadOnlyDictionary<string, DiagnosticDescriptor> Map = RuleDescriptorFactory.CreateAll<AbstractionBypassRule>();
    public IReadOnlyList<DiagnosticDescriptor> Descriptors { get; } = Map.Values.ToList();

    public void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(AnalyzeAbstractionBypass, SyntaxKind.ObjectCreationExpression);
    }

    private static void AnalyzeAbstractionBypass(SyntaxNodeAnalysisContext ctx) {
        if (ctx.CancellationToken.IsCancellationRequested) return;

        if (ctx.Node is not ObjectCreationExpressionSyntax objectCreation) return;

        var symbolInfo = ctx.SemanticModel.GetSymbolInfo(objectCreation);
        if (symbolInfo.Symbol is not IMethodSymbol ctor) return;

        var containingType = ctor.ContainingType;
        if (containingType is null) return;

        var typeName = containingType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

        if (typeName is "HttpClient" or "System.Net.Http.HttpClient") {
            if (IsInHttpClientImplementation(ctx)) return;
            if (IsInDeprecatedOrGeneratedCode(ctx)) return;

            ctx.ReportDiagnostic(Diagnostic.Create(Map["JCC9003"], objectCreation.GetLocation()));
            return;
        }

        if (typeName is "PhysicalFileSystem") {
            if (IsInFileSystemImplementation(ctx)) return;
            if (IsInDeprecatedOrGeneratedCode(ctx)) return;

            ctx.ReportDiagnostic(Diagnostic.Create(Map["JCC9004"], objectCreation.GetLocation()));
            return;
        }

        if (typeName is "FileSystemWatcher" or "System.IO.FileSystemWatcher") {
            if (IsInFileSystemImplementation(ctx)) return;
            if (IsInDeprecatedOrGeneratedCode(ctx)) return;

            ctx.ReportDiagnostic(Diagnostic.Create(Map["JCC9005"], objectCreation.GetLocation()));
            return;
        }
    }

    private static bool IsInHttpClientImplementation(SyntaxNodeAnalysisContext ctx) {
        var containingType = ctx.SemanticModel.GetEnclosingSymbol(ctx.Node.SpanStart)?.ContainingType;
        if (containingType is null) return false;

        var typeName = containingType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        return typeName.Contains("HttpClientProvider", StringComparison.Ordinal) ||
               typeName.Contains("SharedHttpClient", StringComparison.Ordinal) ||
               typeName.Contains("FileSystemFactory", StringComparison.Ordinal) ||
               typeName.Contains("ApiClient", StringComparison.Ordinal) ||
               typeName.Contains("BridgeApiClient", StringComparison.Ordinal);
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
