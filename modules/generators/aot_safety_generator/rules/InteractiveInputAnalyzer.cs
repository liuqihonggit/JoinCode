namespace AotSafety.Generator.Rules;

/// <summary>
/// 交互输入分析共享逻辑 — JCC2001/2002/2003 共用,检测 Console.Read* 调用是否被 IsInputRedirected 保护。
/// </summary>
internal static class InteractiveInputAnalyzer {
    private static readonly HashSet<string> InteractiveInputMethods = new(StringComparer.Ordinal) {
        "Console.ReadLine",
        "Console.ReadKey",
        "Console.Read",
        "System.Console.ReadLine",
        "System.Console.ReadKey",
        "System.Console.Read",
    };

    public static void Analyze(SyntaxNodeAnalysisContext ctx, DiagnosticDescriptor descriptor, string methodName) {
        var invocation = (InvocationExpressionSyntax)ctx.Node;

        var symbol = ctx.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        if (symbol is null) return;

        var containingType = symbol.ContainingType;
        if (containingType is null) return;

        var fullName = $"{containingType.ContainingNamespace?.ToDisplayString()}.{containingType.Name}.{symbol.Name}";
        if (!InteractiveInputMethods.Contains(fullName)) return;
        if (symbol.Name != methodName) return;

        if (AotSafetyHelpers.IsInsideIsInputRedirectedCheck(invocation)) return;
        if (AotSafetyHelpers.IsInsideIfDebugDirective(invocation)) return;

        ctx.ReportDiagnostic(Diagnostic.Create(descriptor, invocation.GetLocation()));
    }
}
