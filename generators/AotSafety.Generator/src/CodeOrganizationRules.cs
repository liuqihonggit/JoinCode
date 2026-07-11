namespace AotSafety.Generator
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class CodeOrganizationRules : DiagnosticAnalyzer
    {
        private static readonly DiagnosticDescriptor RuleFileTooLong = new(
            "JCC8001",
            "代码组织: 文件行数超过2000行，建议拆分",
            "文件 '{0}' 有 {1} 行，超过2000行阈值。过长的文件难以维护和阅读，应按职责拆分为多个类或使用 partial class 分文件组织。",
            "CodeOrganization",
            DiagnosticSeverity.Warning,
            true,
            "Files exceeding 2000 lines typically carry too many responsibilities. Split strategies: 1) Extract independent classes by responsibility; 2) Use partial class to split one class across files; 3) Extract helper methods to extension classes; 4) Extract nested classes to separate files.");

        private static readonly DiagnosticDescriptor RuleDirectFileIO = new(
            "JCC9001",
            "文件IO: 直接调用 System.IO.File/Directory 应使用 IFileSystem 抽象",
            "直接调用 '{0}' 绕过了 IFileSystem 抽象层，导致测试无法实现0磁盘读写。必须通过构造函数注入 IFileSystem（禁止 new PhysicalFileSystem()），使用对应方法替代。",
            "FileSystemAbstraction",
            DiagnosticSeverity.Error,
            true,
            "所有文件读写必须通过 IFileSystem 接口，生产环境使用 PhysicalFileSystem，测试环境使用 InMemoryFileSystem." +
            "映射关系: File.Exists → fs.FileExists, File.ReadAllText/Async → fs.ReadAllText/Async, " +
            "File.WriteAllText/Async → fs.WriteAllText/Async, File.ReadAllBytes/Async → fs.ReadAllBytes/Async, " +
            "File.WriteAllBytes/Async → fs.WriteAllBytes/Async, File.AppendAllText/Async → fs.AppendAllText/Async, " +
            "File.Delete → fs.DeleteFile, File.Move → fs.MoveFile, File.Copy → fs.CopyFile, " +
            "File.OpenRead → fs.OpenRead, File.Open → fs.Open, new FileStream → fs.CreateStream, " +
            "Directory.Exists → fs.DirectoryExists, Directory.CreateDirectory → fs.CreateDirectory, " +
            "Directory.Delete → fs.DeleteDirectory, Directory.GetFiles → fs.GetFiles, " +
            "Directory.GetDirectories → fs.GetDirectories, Directory.EnumerateFiles → fs.EnumerateFiles, " +
            "Directory.EnumerateDirectories → fs.EnumerateDirectories, Directory.Move → fs.MoveDirectory, " +
            "Directory.GetCurrentDirectory → fs.GetCurrentDirectory, Directory.SetCurrentDirectory → fs.SetCurrentDirectory.");

        private static readonly DiagnosticDescriptor RuleDirectFileStream = new(
            "JCC9002",
            "文件IO: 直接 new FileStream 应使用 IFileSystem.CreateStream 抽象",
            "直接构造 FileStream 绕过了 IFileSystem 抽象层，导致测试无法实现0磁盘读写。必须通过构造函数注入 IFileSystem（禁止 new PhysicalFileSystem()），使用 fs.CreateStream(path, mode, access, share) 替代。",
            "FileSystemAbstraction",
            DiagnosticSeverity.Error,
            true,
            "FileStream 构造函数直接访问磁盘，测试中无法替换为内存实现.使用 IFileSystem.CreateStream 替代.");

        private static readonly DiagnosticDescriptor RuleDirectHttpClient = new(
            "JCC9003",
            "HTTP: 直接 new HttpClient() 应使用 IHttpClientProvider 抽象",
            "直接构造 HttpClient 绕过了 IHttpClientProvider 抽象层，导致 JCC_HTTP_MODE=Mock 环境变量无法生效。必须通过构造函数注入 IHttpClientProvider，使用 provider.GetClient() 替代。DI 容器构建前使用 HttpClientProviderFactory.Create().GetClient()。",
            "HttpAbstraction",
            DiagnosticSeverity.Info,
            true,
            "HttpClient 直接 new 导致: 1) 无法通过 JCC_HTTP_MODE=Mock 切换为模拟实现; 2) 连接池无法共享，Socket 耗尽风险; 3) 测试无法 Mock HTTP 请求." +
            "替代方案: 1) DI 容器构建后: 构造函数注入 IHttpClientProvider，调用 provider.GetClient(); " +
            "2) DI 容器构建前: 使用 HttpClientProviderFactory.Create().GetClient(); " +
            "3) 需要自定义 Handler 的场景(如 PipeHttpMessageHandler/SocketsHttpHandler): 允许自建，但需注释说明原因." +
            "例外: Transport 传输层(V1/V2/SSE)、LLM 层(QueryServiceBase/PipeQueryService)、ApiClient 等需要自定义 Handler 的场景.");

        private static readonly DiagnosticDescriptor RuleDirectPhysicalFileSystem = new(
            "JCC9004",
            "文件IO: 直接 new PhysicalFileSystem() 应使用 IFileSystem 注入或 FileSystemFactory",
            "直接构造 PhysicalFileSystem 绕过了 IFileSystem 抽象层，导致 JCC_FILE_SYSTEM_MODE=InMemory 环境变量无法生效。必须通过构造函数注入 IFileSystem，或使用 FileSystemFactory.Create() 替代。",
            "FileSystemAbstraction",
            DiagnosticSeverity.Info,
            true,
            "PhysicalFileSystem 直接 new 导致: 1) 无法通过 JCC_FILE_SYSTEM_MODE=InMemory 切换为内存实现; 2) 测试无法实现0磁盘读写." +
            "替代方案: 1) DI 容器构建后: 构造函数注入 IFileSystem; " +
            "2) DI 容器构建前: 使用 FileSystemFactory.Create(); " +
            "3) DI 注册处: 已通过环境变量自动切换，无需手动 new.");

        private static readonly DiagnosticDescriptor RuleDirectFileSystemWatcher = new(
            "JCC9005",
            "文件IO: 直接 new FileSystemWatcher() 绕过 IFileSystem 抽象",
            "直接构造 FileSystemWatcher 绕过了 IFileSystem 抽象层，在 InMemory 模式下无法触发变更通知。应使用 IFileSystem.Watch() 方法获取 IFileSystemWatcher。",
            "FileSystemAbstraction",
            DiagnosticSeverity.Error,
            true,
            "FileSystemWatcher 直接 new 导致: 1) InMemory 模式下无法触发变更通知; 2) 测试无法模拟文件变更事件. 替代方案: 使用 IFileSystem.Watch(path, filter) 获取 IFileSystemWatcher, 已支持 PhysicalFileSystem 和 InMemoryFileSystem 两种实现.");

        private static readonly DiagnosticDescriptor RuleTooManyFilesInFolder = new(
            "JCC10001",
            "项目结构: 文件夹内直接暴露文件超过20个应拆分",
            "文件夹 '{0}' 内直接暴露 {1} 个文件，超过20个上限。应按职责拆分为多层子文件夹，每个文件夹内直接暴露文件少于20个。",
            "ProjectStructure",
            DiagnosticSeverity.Warning,
            true,
            "文件夹内直接暴露文件超过20个会降低可导航性和可维护性. 正确做法: 1) 按职责/功能/层级拆分子文件夹; 2) 每个子文件夹内文件数 < 20; 3) 可以多层嵌套. 排除: bin/obj/.x/ 目录和生成的代码.",
            customTags: WellKnownDiagnosticTags.CompilationEnd);

        private static readonly DiagnosticDescriptor RuleMixedFilesAndFolders = new(
            "JCC10002",
            "项目结构: 文件夹内文件和文件夹不应同时存在",
            "文件夹 '{0}' 内同时存在 {1} 个文件和 {2} 个子文件夹，违反纯文件夹或纯文件原则。应将文件移入子文件夹或将子文件夹提升，确保每个文件夹内要么纯文件夹要么纯文件。",
            "ProjectStructure",
            DiagnosticSeverity.Warning,
            true,
            "文件夹内文件和文件夹混放降低代码组织一致性. 正确做法: 1) 纯文件夹模式: 所有直接子项都是文件夹; 2) 纯文件模式: 所有直接子项都是文件; 3) 优先采用纯文件夹模式，文件放入对应子文件夹. 排除: bin/obj/.x/ 目录和生成的代码.",
            customTags: WellKnownDiagnosticTags.CompilationEnd);

        private static readonly DiagnosticDescriptor RuleStringConstantMustBeEnum = new(
            "JCC10005",
            "代码规范: 有限集合的字符串常量应枚举化",
            "switch/match 表达式判断字符串 '{0}' 是魔法字符串模式。应定义枚举 + [EnumValue] 特性，用枚举 switch 替代字符串 switch，提高类型安全性和可维护性。",
            "CodeStyle",
            DiagnosticSeverity.Info,
            true,
            "有限集合的字符串常量（模型名、角色名、状态名等）必须枚举化. 正确做法: 1) 定义枚举 + [EnumValue] 特性; 2) 利用源码生成器自动生成 XxxConstants + XxxExtensions; 3) 用枚举 switch 替代字符串 switch. 例外: 外部 API 的动态字符串、用户输入的任意字符串、仅使用一次的常量.");

        private static readonly DiagnosticDescriptor RuleRedundantKeyValueDictionary = new(
            "JCC10006",
            "代码规范: 禁止手动维护 Key==Value 的冗余映射字典",
            "字典 '{0}' 的 Key 和 Value 完全相同，是冗余映射。应使用枚举数组 + ToValue() 遍历匹配，或直接用枚举 + [EnumValue] 特性由源码生成器自动生成映射。",
            "CodeStyle",
            DiagnosticSeverity.Warning,
            true,
            "当字典的 Key == Value 时（如 \"gpt-4o\" → \"gpt-4o\"），手动维护映射是冗余的. 正确做法: 1) 定义枚举 + [EnumValue] 特性; 2) 用 EnumType[] + ToValue() 遍历匹配; 3) 源码生成器自动生成 FrozenDictionary 映射. 例外: Key != Value 的映射字典是合法的.");

        private static readonly DiagnosticDescriptor RuleHardcodedEnumValueString = new(
            "JCC10007",
            "代码规范: 禁止硬编码 [EnumValue] 定义的字符串常量",
            "字符串字面量 '{0}' 与枚举 '{1}' 的 [EnumValue] 值重复。应通过 XxxExtensions.ToValue()/FromValue() 或 XxxConstants 获取，禁止在消费方重复硬编码相同字符串。",
            "CodeStyle",
            DiagnosticSeverity.Warning,
            true,
            "枚举是唯一数据源. 字符串值由 [EnumValue] 定义一次，所有消费方通过 ToValue()/FromValue()/XxxConstants 获取. 在消费方重复硬编码相同字符串会导致: 1) 枚举值变更时多处同步修改; 2) 拼写错误无法编译期检测; 3) 违反 DRY 原则. 例外: 外部协议字符串、日志/异常消息中的描述性文本.");

        private static readonly DiagnosticDescriptor RulePublicMemberMissingXmlDoc = new(
            "JCC10004",
            "代码规范: 公开成员必须包含 XML 文档注释",
            "公开成员 '{0}' 缺少 XML 文档注释。所有 public 方法、属性和构造函数都应提供 <summary> 注释，以确保 IntelliSense 信息完整。",
            "CodeStyle",
            DiagnosticSeverity.Warning,
            true,
            "Public members should have XML documentation comments. This ensures IntelliSense provides meaningful descriptions and serves as code contract documentation. Exceptions: override members, constructors with no parameters, and private/internal members.");

        private static readonly DiagnosticDescriptor RulePropertyReturnsNew = new(
            "JCC10008",
            "代码规范: 属性禁止返回 new 表达式",
            "属性 '{0}' 返回 new 表达式，每次访问都会创建新对象。属性应快速、无副作用、可重复调用返回相同结果。请改为: 1) 方法（如 GetXxx()）如果语义是工厂；2) 缓存字段（如 _cachedXxx ??= BuildXxx()）如果语义是延迟计算。",
            "CodeStyle",
            DiagnosticSeverity.Warning,
            true,
            "Property access should be fast, side-effect-free, and return the same result on repeated calls (given the same state). Returning new violates these conventions: 1) each access allocates a new object causing GC pressure; 2) multiple accesses return different instances violating intuition; 3) layout engine and other high-frequency access scenarios suffer performance degradation. Exceptions: Array.Empty<T>() and other cached factory methods, record With method return values.");

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

        private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
        {
            "bin", "obj", ".x", ".vs", ".idea", ".git", "node_modules",
        };

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(
                RuleFileTooLong,
                RuleDirectFileIO, RuleDirectFileStream,
                RuleDirectHttpClient, RuleDirectPhysicalFileSystem, RuleDirectFileSystemWatcher,
                RuleTooManyFilesInFolder, RuleMixedFilesAndFolders,
                RulePublicMemberMissingXmlDoc,
                RuleStringConstantMustBeEnum, RuleRedundantKeyValueDictionary,
                RuleHardcodedEnumValueString, RulePropertyReturnsNew);

        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterSyntaxNodeAction(AnalyzeDirectFileIO, SyntaxKind.InvocationExpression, SyntaxKind.ObjectCreationExpression);
            context.RegisterSyntaxNodeAction(AnalyzeDirectDirectoryProperty, SyntaxKind.SimpleMemberAccessExpression);
            context.RegisterSyntaxNodeAction(AnalyzeAbstractionBypass, SyntaxKind.ObjectCreationExpression);
            context.RegisterSyntaxTreeAction(AnalyzeFileTooLong);
            context.RegisterCompilationAction(AnalyzeProjectStructure);
            context.RegisterSyntaxNodeAction(AnalyzeStringMatchExpression, SyntaxKind.SwitchExpression);
            context.RegisterSyntaxNodeAction(AnalyzeRedundantKeyValueDictionary, SyntaxKind.FieldDeclaration, SyntaxKind.PropertyDeclaration);
            context.RegisterCompilationStartAction(AnalyzeHardcodedEnumValueStrings);
            context.RegisterSyntaxNodeAction(AnalyzePropertyReturnsNew, SyntaxKind.PropertyDeclaration);
            context.RegisterSyntaxNodeAction(AnalyzePublicMemberXmlDoc, SyntaxKind.MethodDeclaration, SyntaxKind.PropertyDeclaration, SyntaxKind.ConstructorDeclaration);
        }

        private static void AnalyzeFileTooLong(SyntaxTreeAnalysisContext ctx)
        {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            var tree = ctx.Tree;
            var lineCount = tree.GetText().Lines.Count;

            if (lineCount <= 2000) return;

            var filePath = tree.FilePath;
            var lastSlash = filePath.LastIndexOfAny(new[] { '\\', '/' });
            var fileName = lastSlash >= 0 ? filePath.Substring(lastSlash + 1) : filePath;
            var location = tree.GetRoot().GetLocation();

            ctx.ReportDiagnostic(Diagnostic.Create(RuleFileTooLong, location, fileName, lineCount));
        }

        private static void AnalyzeDirectFileIO(SyntaxNodeAnalysisContext ctx)
        {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            if (IsInFileSystemImplementation(ctx)) return;

            if (IsInDeprecatedOrGeneratedCode(ctx)) return;

            switch (ctx.Node)
            {
                case InvocationExpressionSyntax invocation:
                    AnalyzeFileDirectoryInvocation(ctx, invocation);
                    break;
                case ObjectCreationExpressionSyntax objectCreation:
                    AnalyzeFileStreamCreation(ctx, objectCreation);
                    break;
            }
        }

        private static void AnalyzeDirectDirectoryProperty(SyntaxNodeAnalysisContext ctx)
        {
            if (ctx.CancellationToken.IsCancellationRequested) return;
            if (IsInFileSystemImplementation(ctx)) return;
            if (IsInDeprecatedOrGeneratedCode(ctx)) return;

            if (ctx.Node is not MemberAccessExpressionSyntax memberAccess) return;

            var symbolInfo = ctx.SemanticModel.GetSymbolInfo(memberAccess);
            if (symbolInfo.Symbol is not IMethodSymbol method) return;

            var containingType = method.ContainingType;
            if (containingType is null) return;

            var typeName = containingType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

            if ((typeName is "File" or "System.IO.File") && FileMethods.Contains(method.Name))
            {
                ctx.ReportDiagnostic(Diagnostic.Create(RuleDirectFileIO, memberAccess.GetLocation(), $"File.{method.Name}"));
            }
            else if ((typeName is "Directory" or "System.IO.Directory") && DirectoryMethods.Contains(method.Name))
            {
                ctx.ReportDiagnostic(Diagnostic.Create(RuleDirectFileIO, memberAccess.GetLocation(), $"Directory.{method.Name}"));
            }
        }

        private static void AnalyzeFileDirectoryInvocation(SyntaxNodeAnalysisContext ctx, InvocationExpressionSyntax invocation)
        {
            var symbolInfo = ctx.SemanticModel.GetSymbolInfo(invocation);
            if (symbolInfo.Symbol is not IMethodSymbol method) return;

            var containingType = method.ContainingType;
            if (containingType is null) return;

            var typeName = containingType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

            if ((typeName is "File" or "System.IO.File") && FileMethods.Contains(method.Name))
            {
                ctx.ReportDiagnostic(Diagnostic.Create(RuleDirectFileIO, invocation.GetLocation(), $"File.{method.Name}"));
            }
            else if ((typeName is "Directory" or "System.IO.Directory") && DirectoryMethods.Contains(method.Name))
            {
                ctx.ReportDiagnostic(Diagnostic.Create(RuleDirectFileIO, invocation.GetLocation(), $"Directory.{method.Name}"));
            }
        }

        private static void AnalyzeFileStreamCreation(SyntaxNodeAnalysisContext ctx, ObjectCreationExpressionSyntax objectCreation)
        {
            var symbolInfo = ctx.SemanticModel.GetSymbolInfo(objectCreation);
            if (symbolInfo.Symbol is not IMethodSymbol ctor) return;

            var containingType = ctor.ContainingType;
            if (containingType is null) return;

            var typeName = containingType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            if (typeName is "FileStream" or "System.IO.FileStream")
            {
                ctx.ReportDiagnostic(Diagnostic.Create(RuleDirectFileStream, objectCreation.GetLocation()));
            }
        }

        private static void AnalyzeAbstractionBypass(SyntaxNodeAnalysisContext ctx)
        {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            if (ctx.Node is not ObjectCreationExpressionSyntax objectCreation) return;

            var symbolInfo = ctx.SemanticModel.GetSymbolInfo(objectCreation);
            if (symbolInfo.Symbol is not IMethodSymbol ctor) return;

            var containingType = ctor.ContainingType;
            if (containingType is null) return;

            var typeName = containingType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

            // JCC9003: new HttpClient() 绕过 IHttpClientProvider
            if (typeName is "HttpClient" or "System.Net.Http.HttpClient")
            {
                if (IsInHttpClientImplementation(ctx)) return;
                if (IsInDeprecatedOrGeneratedCode(ctx)) return;

                ctx.ReportDiagnostic(Diagnostic.Create(RuleDirectHttpClient, objectCreation.GetLocation()));
                return;
            }

            // JCC9004: new PhysicalFileSystem() 绕过 IFileSystem 注入
            if (typeName is "PhysicalFileSystem")
            {
                if (IsInFileSystemImplementation(ctx)) return;
                if (IsInDeprecatedOrGeneratedCode(ctx)) return;

                ctx.ReportDiagnostic(Diagnostic.Create(RuleDirectPhysicalFileSystem, objectCreation.GetLocation()));
                return;
            }

            // JCC9005: new FileSystemWatcher() 绕过 IFileSystem 抽象
            if (typeName is "FileSystemWatcher" or "System.IO.FileSystemWatcher")
            {
                if (IsInFileSystemImplementation(ctx)) return;
                if (IsInDeprecatedOrGeneratedCode(ctx)) return;

                ctx.ReportDiagnostic(Diagnostic.Create(RuleDirectFileSystemWatcher, objectCreation.GetLocation()));
                return;
            }
        }

        private static bool IsInHttpClientImplementation(SyntaxNodeAnalysisContext ctx)
        {
            var filePath = ctx.Node.SyntaxTree.FilePath;
            if (string.IsNullOrEmpty(filePath)) return false;

            // Transport 传输层和 LLM 层需要自定义 Handler，允许自建 HttpClient
            if (filePath.Contains("\\Transport\\Impl\\", StringComparison.Ordinal) ||
                filePath.Contains("/Transport/Impl/", StringComparison.Ordinal) ||
                filePath.Contains("\\Llm\\src\\Adapters\\", StringComparison.Ordinal) ||
                filePath.Contains("/Llm/src/Adapters/", StringComparison.Ordinal) ||
                filePath.Contains("\\Http\\", StringComparison.Ordinal) ||
                filePath.Contains("/Http/", StringComparison.Ordinal) ||
                filePath.Contains("HttpClientProviderFactory", StringComparison.Ordinal) ||
                filePath.Contains("DefaultHttpClientProvider", StringComparison.Ordinal) ||
                filePath.Contains("MockHttpClientProvider", StringComparison.Ordinal) ||
                filePath.Contains("SharedHttpClient", StringComparison.Ordinal) ||
                filePath.Contains("ApiClient.cs", StringComparison.Ordinal) ||
                filePath.Contains("BridgeApiClient.cs", StringComparison.Ordinal))
                return true;

            var containingType = ctx.SemanticModel.GetEnclosingSymbol(ctx.Node.SpanStart)?.ContainingType;
            if (containingType is null) return false;

            var typeName = containingType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            return typeName.Contains("HttpClientProvider", StringComparison.Ordinal) ||
                   typeName.Contains("SharedHttpClient", StringComparison.Ordinal) ||
                   typeName.Contains("FileSystemFactory", StringComparison.Ordinal) ||
                   typeName.Contains("ApiClient", StringComparison.Ordinal) ||
                   typeName.Contains("BridgeApiClient", StringComparison.Ordinal);
        }

        private static bool IsInFileSystemImplementation(SyntaxNodeAnalysisContext ctx)
        {
            var filePath = ctx.Node.SyntaxTree.FilePath;
            if (string.IsNullOrEmpty(filePath)) return false;

            if (filePath.Contains("\\FileSystem\\", StringComparison.Ordinal) ||
                filePath.Contains("/FileSystem/", StringComparison.Ordinal) ||
                filePath.Contains("SimpleFileReader", StringComparison.Ordinal) ||
                filePath.Contains("FileOperationService", StringComparison.Ordinal))
                return true;

            var containingType = ctx.SemanticModel.GetEnclosingSymbol(ctx.Node.SpanStart)?.ContainingType;
            if (containingType is null) return false;

            var typeName = containingType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            return typeName.Contains("PhysicalFileSystem", StringComparison.Ordinal) ||
                   typeName.Contains("InMemoryFileSystem", StringComparison.Ordinal) ||
                   typeName.Contains("SimpleFileReader", StringComparison.Ordinal) ||
                   typeName.Contains("FileReader", StringComparison.Ordinal) ||
                   typeName.Contains("FileOperationService", StringComparison.Ordinal);
        }

        private static bool IsInDeprecatedOrGeneratedCode(SyntaxNodeAnalysisContext ctx)
        {
            var containingType = ctx.SemanticModel.GetEnclosingSymbol(ctx.Node.SpanStart)?.ContainingType;
            if (containingType is not null)
            {
                foreach (var attr in containingType.GetAttributes())
                {
                    if (attr.AttributeClass?.Name == "ObsoleteAttribute")
                        return true;
                }
            }

            var containingMethod = ctx.SemanticModel.GetEnclosingSymbol(ctx.Node.SpanStart) as IMethodSymbol;
            if (containingMethod is not null)
            {
                foreach (var attr in containingMethod.GetAttributes())
                {
                    if (attr.AttributeClass?.Name == "ObsoleteAttribute")
                        return true;
                }
            }

            return false;
        }

        private static void AnalyzeProjectStructure(CompilationAnalysisContext ctx)
        {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            var compilation = ctx.Compilation;
            var syntaxTrees = compilation.SyntaxTrees;

            var directoryFiles = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var directorySubdirs = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var tree in syntaxTrees)
            {
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
                if (parentSlash >= 0)
                {
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

            foreach (var dir in allDirs)
            {
                if (ctx.CancellationToken.IsCancellationRequested) return;

                var fileCount = directoryFiles.TryGetValue(dir, out var files) ? files.Count : 0;
                if (fileCount > 20)
                {
                    var lastSlash = dir.LastIndexOfAny(new[] { '\\', '/' });
                    var dirName = lastSlash >= 0 ? dir.Substring(lastSlash + 1) : dir;
                    var location = compilation.SyntaxTrees
                        .FirstOrDefault(t => t.FilePath.StartsWith(dir, StringComparison.OrdinalIgnoreCase))
                        ?.GetRoot().GetLocation();
                    if (location is not null)
                        ctx.ReportDiagnostic(Diagnostic.Create(RuleTooManyFilesInFolder, location, dirName, fileCount));
                }
            }

            foreach (var dir in allDirs)
            {
                if (ctx.CancellationToken.IsCancellationRequested) return;

                var fileCount = directoryFiles.TryGetValue(dir, out var files) ? files.Count : 0;
                var subdirCount = directorySubdirs.TryGetValue(dir, out var subdirs) ? subdirs.Count : 0;

                if (fileCount > 0 && subdirCount > 0)
                {
                    var lastSlash = dir.LastIndexOfAny(new[] { '\\', '/' });
                    var dirName = lastSlash >= 0 ? dir.Substring(lastSlash + 1) : dir;
                    var location = compilation.SyntaxTrees
                        .FirstOrDefault(t => t.FilePath.StartsWith(dir, StringComparison.OrdinalIgnoreCase))
                        ?.GetRoot().GetLocation();
                    if (location is not null)
                        ctx.ReportDiagnostic(Diagnostic.Create(RuleMixedFilesAndFolders, location, dirName, fileCount, subdirCount));
                }
            }
        }

        private static bool IsInExcludedPath(string filePath)
        {
            var parts = filePath.Split(new[] { '\\', '/' });
            foreach (var part in parts)
            {
                if (ExcludedDirectories.Contains(part))
                    return true;
            }
            return false;
        }

        private static void AnalyzeStringMatchExpression(SyntaxNodeAnalysisContext ctx)
        {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            var switchExpr = (SwitchExpressionSyntax)ctx.Node;

            var governingType = ctx.SemanticModel.GetTypeInfo(switchExpr.GoverningExpression).Type;
            if (governingType is null || governingType.SpecialType != SpecialType.System_String) return;

            if (switchExpr.Arms.Count < 3) return;

            var location = switchExpr.GoverningExpression.GetLocation();
            var exprText = switchExpr.GoverningExpression.ToString();
            if (exprText.Length > 30) exprText = exprText.Substring(0, 30) + "...";

            ctx.ReportDiagnostic(Diagnostic.Create(RuleStringConstantMustBeEnum, location, exprText));
        }

        private static void AnalyzeRedundantKeyValueDictionary(SyntaxNodeAnalysisContext ctx)
        {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            TypeSyntax? typeSyntax = null;
            VariableDeclaratorSyntax? variableDeclarator = null;
            string? variableName = null;

            switch (ctx.Node)
            {
                case FieldDeclarationSyntax field:
                    typeSyntax = field.Declaration.Type;
                    variableDeclarator = field.Declaration.Variables.FirstOrDefault();
                    break;
                case PropertyDeclarationSyntax property:
                    typeSyntax = property.Type;
                    variableName = property.Identifier.ValueText;
                    break;
            }

            if (typeSyntax is null) return;

            var typeSymbol = ctx.SemanticModel.GetTypeInfo(typeSyntax).Type as INamedTypeSymbol;
            if (typeSymbol is null) return;

            if (!IsDictionaryStringString(typeSymbol)) return;

            if (variableDeclarator is not null)
                variableName = variableDeclarator.Identifier.ValueText;

            if (string.IsNullOrEmpty(variableName)) return;

            var hasRedundantKV = false;
            var initializer = variableDeclarator?.Initializer?.Value;

            if (initializer is ObjectCreationExpressionSyntax objCreation &&
                objCreation.ArgumentList?.Arguments.Count == 0)
            {
                if (objCreation.Initializer is not null)
                {
                    foreach (var init in objCreation.Initializer.Expressions)
                    {
                        if (init is InitializerExpressionSyntax collectionInit)
                        {
                            foreach (var item in collectionInit.Expressions)
                            {
                                if (IsKeyValueSame(item))
                                {
                                    hasRedundantKV = true;
                                    break;
                                }
                            }
                        }
                    }
                }
            }

            if (!hasRedundantKV) return;

            var location = ctx.Node.GetLocation();
            ctx.ReportDiagnostic(Diagnostic.Create(RuleRedundantKeyValueDictionary, location, variableName));
        }

        private static bool IsDictionaryStringString(INamedTypeSymbol type)
        {
            if (!type.IsGenericType) return false;
            var def = type.ConstructedFrom;
            if (def is null) return false;
            var fullName = $"{def.ContainingNamespace?.ToDisplayString()}.{def.Name}";
            if (fullName != "System.Collections.Generic.Dictionary") return false;
            if (type.TypeArguments.Length != 2) return false;
            return type.TypeArguments[0].SpecialType == SpecialType.System_String &&
                   type.TypeArguments[1].SpecialType == SpecialType.System_String;
        }

        private static bool IsKeyValueSame(ExpressionSyntax expression)
        {
            if (expression is not ParenthesizedLambdaExpressionSyntax lambda) return false;

            if (lambda.ParameterList.Parameters.Count != 2) return false;

            if (lambda.Body is not InvocationExpressionSyntax invocation) return false;

            var args = invocation.ArgumentList.Arguments;
            if (args.Count != 2) return false;

            var keyExpr = args[0].Expression.ToString().Trim();
            var valueExpr = args[1].Expression.ToString().Trim();

            return keyExpr == valueExpr;
        }

        private static void AnalyzeHardcodedEnumValueStrings(CompilationStartAnalysisContext ctx)
        {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            var enumValueStrings = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

            foreach (var tree in ctx.Compilation.SyntaxTrees)
            {
                if (ctx.CancellationToken.IsCancellationRequested) return;

                var root = tree.GetRoot();

                foreach (var attr in root.DescendantNodes().OfType<AttributeSyntax>())
                {
                    var attrName = attr.Name.ToString().Replace(" ", "");
                    if (attrName != "EnumValue" && attrName != "EnumValueAttribute") continue;

                    var args = attr.ArgumentList?.Arguments;
                    if (args is null || args.Value.Count == 0) continue;

                    var firstArg = args.Value[0].Expression;
                    if (firstArg is not LiteralExpressionSyntax literal || !literal.IsKind(SyntaxKind.StringLiteralExpression))
                        continue;

                    var stringValue = literal.Token.ValueText;

                    var parentEnum = attr.Parent?.Parent;
                    if (parentEnum is EnumMemberDeclarationSyntax enumMember)
                    {
                        var enumType = enumMember.Parent as EnumDeclarationSyntax;
                        var enumName = enumType?.Identifier.ValueText ?? "Unknown";
                        enumValueStrings.TryAdd(stringValue, enumName);
                    }
                }
            }

            if (enumValueStrings.IsEmpty) return;

            ctx.RegisterSyntaxNodeAction(nodeCtx =>
            {
                if (nodeCtx.CancellationToken.IsCancellationRequested) return;

                if (nodeCtx.Node is not LiteralExpressionSyntax literal ||
                    !literal.IsKind(SyntaxKind.StringLiteralExpression))
                    return;

                var stringValue = literal.Token.ValueText;

                if (!enumValueStrings.TryGetValue(stringValue, out var enumName)) return;

                if (IsInEnumDefinition(literal)) return;

                var location = literal.GetLocation();
                nodeCtx.ReportDiagnostic(Diagnostic.Create(RuleHardcodedEnumValueString, location, stringValue, enumName));
            }, SyntaxKind.StringLiteralExpression);
        }

        private static bool IsInEnumDefinition(SyntaxNode node)
        {
            var current = node.Parent;
            while (current is not null)
            {
                if (current is EnumDeclarationSyntax or EnumMemberDeclarationSyntax)
                    return true;
                if (current is AttributeArgumentSyntax)
                    return true;
                if (current is AttributeSyntax)
                    return true;
                current = current.Parent;
            }
            return false;
        }

        private static void AnalyzePropertyReturnsNew(SyntaxNodeAnalysisContext ctx)
        {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            var prop = (PropertyDeclarationSyntax)ctx.Node;

            if (prop.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword)))
                return;

            if (prop.ExplicitInterfaceSpecifier is not null)
                return;

            if (prop.Parent is InterfaceDeclarationSyntax)
                return;

            var symbol = ctx.SemanticModel.GetDeclaredSymbol(prop, ctx.CancellationToken);
            if (symbol is not null)
            {
                if (symbol.ExplicitInterfaceImplementations.Length > 0)
                    return;

                if (ImplementsInterfaceProperty(symbol))
                    return;
            }

            if (prop.ExpressionBody is not null)
            {
                if (ContainsNewExpression(prop.ExpressionBody.Expression))
                {
                    var propName = prop.Identifier.ValueText;
                    ctx.ReportDiagnostic(Diagnostic.Create(RulePropertyReturnsNew, prop.Identifier.GetLocation(), propName));
                }
                return;
            }

            if (prop.AccessorList is null) return;

            foreach (var accessor in prop.AccessorList.Accessors)
            {
                if (!accessor.Keyword.IsKind(SyntaxKind.GetKeyword)) continue;

                if (accessor.ExpressionBody is not null)
                {
                    if (ContainsNewExpression(accessor.ExpressionBody.Expression))
                    {
                        var propName = prop.Identifier.ValueText;
                        ctx.ReportDiagnostic(Diagnostic.Create(RulePropertyReturnsNew, prop.Identifier.GetLocation(), propName));
                        return;
                    }
                }
                else if (accessor.Body is not null)
                {
                    foreach (var stmt in accessor.Body.Statements)
                    {
                        if (stmt is ReturnStatementSyntax returnStmt && returnStmt.Expression is not null)
                        {
                            if (ContainsNewExpression(returnStmt.Expression))
                            {
                                var propName = prop.Identifier.ValueText;
                                ctx.ReportDiagnostic(Diagnostic.Create(RulePropertyReturnsNew, prop.Identifier.GetLocation(), propName));
                                return;
                            }
                        }
                    }
                }
            }
        }

        private static bool ContainsNewExpression(ExpressionSyntax expression)
        {
            if (expression is ObjectCreationExpressionSyntax)
                return true;

            if (expression is ImplicitObjectCreationExpressionSyntax)
                return true;

            if (expression is InvocationExpressionSyntax invocation)
            {
                var name = invocation.Expression.ToString();
                if (name.StartsWith("Array.Empty") || name.StartsWith("Array.Empty<"))
                    return false;

                foreach (var arg in invocation.ArgumentList.Arguments)
                {
                    if (ContainsNewExpression(arg.Expression))
                        return true;
                }
                return false;
            }

            switch (expression)
            {
                case BinaryExpressionSyntax binary:
                    return ContainsNewExpression(binary.Left) || ContainsNewExpression(binary.Right);
                case ConditionalExpressionSyntax conditional:
                    return ContainsNewExpression(conditional.WhenTrue) || ContainsNewExpression(conditional.WhenFalse);
                case SwitchExpressionSyntax switchExpr:
                    foreach (var arm in switchExpr.Arms)
                    {
                        if (ContainsNewExpression(arm.Expression))
                            return true;
                    }
                    return false;
                case ParenthesizedExpressionSyntax parenthesized:
                    return ContainsNewExpression(parenthesized.Expression);
                case CastExpressionSyntax cast:
                    return ContainsNewExpression(cast.Expression);
                case InitializerExpressionSyntax initializer:
                    foreach (var expr in initializer.Expressions)
                    {
                        if (ContainsNewExpression(expr))
                            return true;
                    }
                    return false;
                default:
                    return false;
            }
        }

        private static bool ImplementsInterfaceProperty(IPropertySymbol property)
        {
            var containingType = property.ContainingType;
            if (containingType is null) return false;

            foreach (var iface in containingType.AllInterfaces)
            {
                foreach (var member in iface.GetMembers(property.Name))
                {
                    if (member is IPropertySymbol)
                        return true;
                }
            }

            return false;
        }

        private static void AnalyzePublicMemberXmlDoc(SyntaxNodeAnalysisContext ctx)
        {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            ISymbol? symbol = null;
            Location? location = null;
            string? memberName = null;

            switch (ctx.Node)
            {
                case MethodDeclarationSyntax method:
                    symbol = ctx.SemanticModel.GetDeclaredSymbol(method, ctx.CancellationToken);
                    if (symbol is null) return;
                    if (symbol.DeclaredAccessibility != Accessibility.Public) return;
                    if (method.Modifiers.Any(m => m.IsKind(SyntaxKind.OverrideKeyword))) return;
                    if (HasXmlDoc(method)) return;
                    location = method.Identifier.GetLocation();
                    memberName = symbol.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);
                    break;

                case PropertyDeclarationSyntax property:
                    symbol = ctx.SemanticModel.GetDeclaredSymbol(property, ctx.CancellationToken);
                    if (symbol is null) return;
                    if (symbol.DeclaredAccessibility != Accessibility.Public) return;
                    if (property.Modifiers.Any(m => m.IsKind(SyntaxKind.OverrideKeyword))) return;
                    if (HasXmlDoc(property)) return;
                    location = property.Identifier.GetLocation();
                    memberName = symbol.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);
                    break;

                case ConstructorDeclarationSyntax ctor:
                    symbol = ctx.SemanticModel.GetDeclaredSymbol(ctor, ctx.CancellationToken);
                    if (symbol is null) return;
                    if (symbol.DeclaredAccessibility != Accessibility.Public) return;
                    if (ctor.ParameterList.Parameters.Count == 0) return;
                    if (HasXmlDoc(ctor)) return;
                    location = ctor.Identifier.GetLocation();
                    memberName = symbol.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);
                    break;

                default:
                    return;
            }

            if (memberName is not null && location is not null)
                ctx.ReportDiagnostic(Diagnostic.Create(RulePublicMemberMissingXmlDoc, location, memberName));
        }

        private static bool HasXmlDoc(MemberDeclarationSyntax member)
        {
            foreach (var trivia in member.GetLeadingTrivia())
            {
                if (trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
                    trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
                    return true;
            }
            return false;
        }
    }
}
