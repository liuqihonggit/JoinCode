namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC4008: 并发红线 — 禁止显式锁原语（lock/Monitor/信号量/互斥量/读写锁/锁生态类型）。
/// 项目并发模型为 Actor 单写者邮箱、不可变快照 + CAS、Channel 管道与原子文件租约；
/// 显式锁会隐藏共享状态所有权，导致重入歧义、死锁与跨进程僵死。
/// 存量文件通过 lock-primitive-baseline.txt 临时豁免，基线只减不增，B6 归档锁生态时清零。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "Concurrency",
    Id = "JCC4008",
    Title = "并发红线: 禁止 {0}",
    Description = "检测到 {0}。项目并发模型为 Actor 单写者邮箱、不可变快照 + CAS、Channel 管道与原子文件租约。{1}。显式锁会隐藏共享状态所有权，导致重入歧义、死锁与跨进程僵死。",
    Category = "ConcurrencySafety",
    Severity = DiagnosticSeverity.Error,
    IsEnabledByDefault = true,
    HelpLinkUri = "并发零锁红线.正确模式: 用 Actor 单写者邮箱串行化状态变更; 用不可变快照 + Interlocked.CompareExchange 做无锁读改写; 用 Channel<T> 做生产者/消费者与背压; 用 TCS 做单飞; 跨进程互斥用 FileMode.CreateNew 原子文件租约 + TTL 心跳.禁止 lock/Monitor/SemaphoreSlim/Semaphore/Mutex/ReaderWriterLockSlim/SpinLock/事件等待句柄以及项目内 AsyncLock/LockRef/AsyncLockedDictionary/LockRegistry/TimeoutLock/OrderedLockManager.")]
public sealed class ForbiddenLockPrimitiveRule : AnalyzerRuleBase<ForbiddenLockPrimitiveRule> {
    private const string BaselineFileName = "lock-primitive-baseline.txt";

    private static readonly ImmutableHashSet<string> BannedBclTypes = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "System.Threading.SemaphoreSlim",
        "System.Threading.Semaphore",
        "System.Threading.Mutex",
        "System.Threading.ReaderWriterLockSlim",
        "System.Threading.SpinLock",
        "System.Threading.ManualResetEventSlim",
        "System.Threading.ManualResetEvent",
        "System.Threading.AutoResetEvent",
        "System.Threading.CountdownEvent",
        "System.Threading.Barrier",
        "System.Threading.Lock");

    private static readonly ImmutableHashSet<string> BannedProjectTypes = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "AsyncLock",
        "AsyncLockedDictionary",
        "LockRef",
        "LockRegistry",
        "LockTimeoutPolicy",
        "TimeoutLock",
        "OrderedLockManager");

    private static readonly ImmutableHashSet<string> BannedMonitorMethods = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "Enter",
        "TryEnter",
        "Exit",
        "Pulse",
        "PulseAll",
        "Wait");

    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        var baseline = LoadBaseline(context.Options.AdditionalFiles, context.CancellationToken);
        var reported = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);

        context.RegisterSyntaxNodeAction(
            ctx => AnalyzeLockStatement(ctx, baseline, reported),
            SyntaxKind.LockStatement);

        context.RegisterSyntaxNodeAction(
            ctx => AnalyzeMonitorInvocation(ctx, baseline, reported),
            SyntaxKind.InvocationExpression);

        context.RegisterSyntaxNodeAction(
            ctx => AnalyzeVariableDeclaration(ctx, baseline, reported),
            SyntaxKind.VariableDeclaration);

        context.RegisterSyntaxNodeAction(
            ctx => AnalyzePropertyDeclaration(ctx, baseline, reported),
            SyntaxKind.PropertyDeclaration);

        context.RegisterSyntaxNodeAction(
            ctx => AnalyzeParameter(ctx, baseline, reported),
            SyntaxKind.Parameter);

        context.RegisterSyntaxNodeAction(
            ctx => AnalyzeMethodDeclaration(ctx, baseline, reported),
            SyntaxKind.MethodDeclaration);

        context.RegisterSyntaxNodeAction(
            ctx => AnalyzeLocalFunction(ctx, baseline, reported),
            SyntaxKind.LocalFunctionStatement);

        context.RegisterSyntaxNodeAction(
            ctx => AnalyzeObjectCreation(ctx, baseline, reported),
            SyntaxKind.ObjectCreationExpression);

        context.RegisterSyntaxNodeAction(
            ctx => AnalyzeImplicitObjectCreation(ctx, baseline, reported),
            SyntaxKind.ImplicitObjectCreationExpression);
    }

    private static void AnalyzeLockStatement(
        SyntaxNodeAnalysisContext context,
        ImmutableHashSet<string> baseline,
        ConcurrentDictionary<string, byte> reported) {
        if (context.CancellationToken.IsCancellationRequested) return;

        var statement = (LockStatementSyntax)context.Node;
        Report(
            context,
            baseline,
            reported,
            statement.LockKeyword.GetLocation(),
            "lock 语句",
            "改用 Actor 单写者邮箱（Channel<T>）或不可变快照 + Interlocked.CompareExchange");
    }

    private static void AnalyzeMonitorInvocation(
        SyntaxNodeAnalysisContext context,
        ImmutableHashSet<string> baseline,
        ConcurrentDictionary<string, byte> reported) {
        if (context.CancellationToken.IsCancellationRequested) return;

        var invocation = (InvocationExpressionSyntax)context.Node;
        var method = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol as IMethodSymbol;
        if (method is null) return;
        if (method.ContainingType?.ToDisplayString() != "System.Threading.Monitor") return;
        if (!BannedMonitorMethods.Contains(method.Name)) return;

        Report(
            context,
            baseline,
            reported,
            invocation.Expression.GetLocation(),
            $"Monitor.{method.Name}",
            "改用 Actor 单写者邮箱（Channel<T>）或不可变快照 + Interlocked.CompareExchange");
    }

    private static void AnalyzeVariableDeclaration(
        SyntaxNodeAnalysisContext context,
        ImmutableHashSet<string> baseline,
        ConcurrentDictionary<string, byte> reported) {
        if (context.CancellationToken.IsCancellationRequested) return;

        var declaration = (VariableDeclarationSyntax)context.Node;
        foreach (var variable in declaration.Variables) {
            var symbol = context.SemanticModel.GetDeclaredSymbol(variable, context.CancellationToken);
            var type = symbol switch {
                IFieldSymbol field => field.Type,
                ILocalSymbol local => local.Type,
                _ => null
            };
            CheckType(context, baseline, reported, type, variable.Identifier.GetLocation());
        }
    }

    private static void AnalyzePropertyDeclaration(
        SyntaxNodeAnalysisContext context,
        ImmutableHashSet<string> baseline,
        ConcurrentDictionary<string, byte> reported) {
        if (context.CancellationToken.IsCancellationRequested) return;

        var property = (PropertyDeclarationSyntax)context.Node;
        var symbol = context.SemanticModel.GetDeclaredSymbol(property, context.CancellationToken) as IPropertySymbol;
        CheckType(context, baseline, reported, symbol?.Type, property.Type.GetLocation());
    }

    private static void AnalyzeParameter(
        SyntaxNodeAnalysisContext context,
        ImmutableHashSet<string> baseline,
        ConcurrentDictionary<string, byte> reported) {
        if (context.CancellationToken.IsCancellationRequested) return;

        var parameter = (ParameterSyntax)context.Node;
        var symbol = context.SemanticModel.GetDeclaredSymbol(parameter, context.CancellationToken) as IParameterSymbol;
        CheckType(context, baseline, reported, symbol?.Type, parameter.Type?.GetLocation() ?? parameter.Identifier.GetLocation());
    }

    private static void AnalyzeMethodDeclaration(
        SyntaxNodeAnalysisContext context,
        ImmutableHashSet<string> baseline,
        ConcurrentDictionary<string, byte> reported) {
        if (context.CancellationToken.IsCancellationRequested) return;

        var method = (MethodDeclarationSyntax)context.Node;
        var symbol = context.SemanticModel.GetDeclaredSymbol(method, context.CancellationToken) as IMethodSymbol;
        CheckType(context, baseline, reported, symbol?.ReturnType, method.ReturnType.GetLocation());
    }

    private static void AnalyzeLocalFunction(
        SyntaxNodeAnalysisContext context,
        ImmutableHashSet<string> baseline,
        ConcurrentDictionary<string, byte> reported) {
        if (context.CancellationToken.IsCancellationRequested) return;

        var function = (LocalFunctionStatementSyntax)context.Node;
        var symbol = context.SemanticModel.GetDeclaredSymbol(function, context.CancellationToken) as IMethodSymbol;
        CheckType(context, baseline, reported, symbol?.ReturnType, function.ReturnType.GetLocation());
    }

    private static void AnalyzeObjectCreation(
        SyntaxNodeAnalysisContext context,
        ImmutableHashSet<string> baseline,
        ConcurrentDictionary<string, byte> reported) {
        if (context.CancellationToken.IsCancellationRequested) return;

        var creation = (ObjectCreationExpressionSyntax)context.Node;
        if (IsCoveredByDeclaration(creation)) return;

        var type = context.SemanticModel.GetTypeInfo(creation, context.CancellationToken).Type;
        CheckType(context, baseline, reported, type, creation.Type.GetLocation());
    }

    private static void AnalyzeImplicitObjectCreation(
        SyntaxNodeAnalysisContext context,
        ImmutableHashSet<string> baseline,
        ConcurrentDictionary<string, byte> reported) {
        if (context.CancellationToken.IsCancellationRequested) return;

        var creation = (ImplicitObjectCreationExpressionSyntax)context.Node;
        if (IsCoveredByDeclaration(creation)) return;

        var typeInfo = context.SemanticModel.GetTypeInfo(creation, context.CancellationToken);
        CheckType(context, baseline, reported, typeInfo.Type ?? typeInfo.ConvertedType, creation.NewKeyword.GetLocation());
    }

    private static bool IsCoveredByDeclaration(ExpressionSyntax creation) {
        return creation.Parent switch {
            EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax } } => true,
            EqualsValueClauseSyntax { Parent: PropertyDeclarationSyntax } => true,
            EqualsValueClauseSyntax { Parent: ParameterSyntax } => true,
            ArrowExpressionClauseSyntax { Parent: MethodDeclarationSyntax } => true,
            ArrowExpressionClauseSyntax { Parent: LocalFunctionStatementSyntax } => true,
            _ => false
        };
    }

    private static void CheckType(
        SyntaxNodeAnalysisContext context,
        ImmutableHashSet<string> baseline,
        ConcurrentDictionary<string, byte> reported,
        ITypeSymbol? type,
        Location location) {
        var original = UnwrapNullable(type)?.OriginalDefinition;
        if (original is null) return;

        var fullName = original.ToDisplayString();
        if (BannedBclTypes.Contains(fullName)) {
            Report(context, baseline, reported, location, original.Name, ReplacementFor(original.Name));
            return;
        }

        if (BannedProjectTypes.Contains(original.Name)) {
            Report(context, baseline, reported, location, original.Name, ReplacementFor(original.Name));
        }
    }

    private static ITypeSymbol? UnwrapNullable(ITypeSymbol? type) {
        if (type is not INamedTypeSymbol named) return type;
        if (named.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T) return type;
        return named.TypeArguments.Length == 1 ? named.TypeArguments[0] : type;
    }

    private static string ReplacementFor(string typeName) {
        return typeName switch {
            "SemaphoreSlim" or "Semaphore" =>
                "互斥改用 Actor/Channel; 限流改用令牌状态 + CAS; 信号改用 Channel 或 TaskCompletionSource",
            "Mutex" or "ReaderWriterLockSlim" or "SpinLock" or "Lock" =>
                "改用 Channel、不可变快照 + CAS 或 Actor 邮箱; 跨进程互斥改用原子文件租约",
            "ManualResetEventSlim" or "ManualResetEvent" or "AutoResetEvent" or "CountdownEvent" or "Barrier" =>
                "改用 Channel、TaskCompletionSource 或 Actor 邮箱",
            "AsyncLock" or "AsyncLockedDictionary" or "LockRef" or "LockRegistry" or "LockTimeoutPolicy" or "TimeoutLock" or "OrderedLockManager" =>
                "改用 Actor/Channel 或不可变快照 + CAS; 该锁生态将归档，禁止新增消费",
            _ => "改用 Actor/Channel 或不可变快照 + CAS"
        };
    }

    private static void Report(
        SyntaxNodeAnalysisContext context,
        ImmutableHashSet<string> baseline,
        ConcurrentDictionary<string, byte> reported,
        Location location,
        string construct,
        string replacement) {
        var path = context.Node.SyntaxTree.FilePath ?? string.Empty;
        if (IsBaselined(path, baseline)) return;

        var line = location.GetLineSpan().StartLinePosition.Line;
        var key = $"{path}|{line}|{construct}";
        if (!reported.TryAdd(key, 0)) return;

        context.ReportDiagnostic(Diagnostic.Create(Descriptor, location, construct, replacement));
    }

    private static ImmutableHashSet<string> LoadBaseline(ImmutableArray<AdditionalText> additionalFiles, CancellationToken cancellationToken) {
        var builder = ImmutableHashSet.CreateBuilder<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in additionalFiles) {
            if (!file.Path.EndsWith(BaselineFileName, StringComparison.OrdinalIgnoreCase)) continue;

            var text = file.GetText(cancellationToken);
            if (text is null) continue;

            foreach (var line in text.Lines) {
                var raw = line.ToString().Trim();
                if (raw.Length == 0 || raw[0] == '#') continue;

                var separator = raw.IndexOf('|');
                var path = (separator >= 0 ? raw.Substring(0, separator) : raw).Trim().Replace('\\', '/');
                if (path.Length > 0) builder.Add(path);
            }
        }

        return builder.ToImmutable();
    }

    private static bool IsBaselined(string filePath, ImmutableHashSet<string> baseline) {
        if (baseline.Count == 0) return false;

        var normalized = filePath.Replace('\\', '/');
        var withPrefix = "/" + normalized;
        foreach (var entry in baseline) {
            if (normalized.Equals(entry, StringComparison.OrdinalIgnoreCase)) return true;
            if (withPrefix.EndsWith("/" + entry, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }
}
