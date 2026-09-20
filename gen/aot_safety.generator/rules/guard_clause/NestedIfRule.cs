namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC1009: 卫语句 — if 嵌套超过2层（3层及以上），建议卫语句扁平化。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "GuardClause",
    Id = "JCC1009",
    Title = "卫语句: if 嵌套超过2层（3层及以上），建议卫语句扁平化",
    Description = "if 嵌套达到 {0} 层，超过最大允许深度 {1} 层。深层嵌套降低可读性，应使用卫语句（early return/continue/throw）扁平化。将内层条件取反后提前返回，主逻辑留在方法顶层。",
    Category = "GuardClause",
    Severity = DiagnosticSeverity.Warning,
    IsEnabledByDefault = true,
    HelpLinkUri = "卫语句模式将嵌套 if 转为线性早返回. ❌ if (a) { if (b) { if (c) { ... } } } ✅ if (!a) return; if (!b) return; if (!c) return; ... " +
    "例外: else-if 链不计入嵌套深度（if (a) {} else if (b) {} else if (c) {} 是 1 层）. " +
    "例外: 生成代码自动跳过. 例外: switch 表达式内部 if 不计入嵌套.")]
public sealed class NestedIfRule : AnalyzerRuleBase<NestedIfRule> {
    private const int MaxAllowedIfNesting = 2;

    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
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
                Descriptor,
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
                if (!IsElseIfChainLink(parentIf, current)) {
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
        var current = (SyntaxNode)ifStmt;
        var parent = ifStmt.Parent;

        while (parent is not null) {
            if (parent is IfStatementSyntax parentIf) {
                if (!IsElseIfChainLink(parentIf, current)) {
                    outermost = parentIf;
                }
            }
            current = parent;
            parent = parent.Parent;
        }
        return outermost;
    }

    /// <summary>
    /// 判断 current 是否处于 parentIf 的 else-if 链中. else-if 链不计入嵌套深度:
    /// if (a) {} else if (b) {} 中, if(b) 是 if(a) 的平级分支而非嵌套.
    /// 两种情况:
    /// 1. current 是 else-if 的 IfStatement 本身 (parentIf.Else.Statement == current 且为 IfStatement)
    /// 2. current 是 ElseClause, 且其 Statement 是 IfStatement (遍历到 else-if 链的边界节点)
    /// </summary>
    private static bool IsElseIfChainLink(IfStatementSyntax parentIf, SyntaxNode current) {
        if (parentIf.Else is not { } elseClause) return false;
        if (elseClause.Statement is not IfStatementSyntax) return false;
        return current == elseClause || current == elseClause.Statement;
    }
}
