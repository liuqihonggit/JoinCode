namespace AotSafety.Generator;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class GuardClauseRules : DiagnosticAnalyzer {
    private const int MaxAllowedIfNesting = 2;

    private static readonly DiagnosticDescriptor RuleNestedIf = new(
        "JCC1009",
        "卫语句: if 嵌套超过2层（3层及以上），建议卫语句扁平化",
        "if 嵌套达到 {0} 层，超过最大允许深度 {1} 层。深层嵌套降低可读性，应使用卫语句（early return/continue/throw）扁平化。将内层条件取反后提前返回，主逻辑留在方法顶层。",
        "GuardClause",
        DiagnosticSeverity.Warning,
        true,
        "卫语句模式将嵌套 if 转为线性早返回. ❌ if (a) { if (b) { if (c) { ... } } } ✅ if (!a) return; if (!b) return; if (!c) return; ... " +
        "例外: else-if 链不计入嵌套深度（if (a) {} else if (b) {} else if (c) {} 是 1 层）. " +
        "例外: 生成代码自动跳过. 例外: switch 表达式内部 if 不计入嵌套.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(RuleNestedIf);

    public override void Initialize(AnalysisContext context) {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext ctx) {
        if (ctx.CancellationToken.IsCancellationRequested) return;

        var method = (MethodDeclarationSyntax)ctx.Node;
        if (method.Body is null) return;

        var outermostToMaxDepth = new Dictionary<IfStatementSyntax, int>();

        foreach (var ifStmt in method.Body.DescendantNodes().OfType<IfStatementSyntax>()) {
            var depth = CountEnclosingIfs(ifStmt);
            if (depth < MaxAllowedIfNesting) continue;

            var outermost = FindOutermostIf(ifStmt);
            if (outermostToMaxDepth.TryGetValue(outermost, out var existing)) {
                if (depth > existing) {
                    outermostToMaxDepth[outermost] = depth;
                }
            } else {
                outermostToMaxDepth[outermost] = depth;
            }
        }

        foreach (var kvp in outermostToMaxDepth) {
            ctx.ReportDiagnostic(Diagnostic.Create(
                RuleNestedIf,
                kvp.Key.IfKeyword.GetLocation(),
                kvp.Value + 1,
                MaxAllowedIfNesting));
        }
    }

    private static int CountEnclosingIfs(IfStatementSyntax ifStmt) {
        var depth = 0;
        var current = (SyntaxNode)ifStmt;
        var parent = ifStmt.Parent;

        while (parent is not null) {
            if (parent is IfStatementSyntax parentIf) {
                if (parentIf.Else is null || parentIf.Else.Statement != current) {
                    depth++;
                }
            }
            current = parent;
            parent = parent.Parent;
        }
        return depth;
    }

    private static IfStatementSyntax FindOutermostIf(IfStatementSyntax ifStmt) {
        var outermost = ifStmt;
        var parent = ifStmt.Parent;

        while (parent is not null) {
            if (parent is IfStatementSyntax parentIf) {
                outermost = parentIf;
            }
            parent = parent.Parent;
        }
        return outermost;
    }
}
