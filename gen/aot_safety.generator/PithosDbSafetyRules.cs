namespace AotSafety.Generator;

/// <summary>
/// PithosDB 安全规则 — 防止回退到有文件锁的 MemoryMappedFile 实现。
/// JCC9310: SSTableReader 中禁止使用 MemoryMappedFile.CreateFromFile，应使用 byte[] + GCHandle.Pinned 替代。
/// 根因: MemoryMappedFile.CreateFromFile 默认 FileShare.None（独占锁），跨进程/同进程多实例读取 SST 文件时 IOException。
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PithosDbSafetyRules : DiagnosticAnalyzer {
    private static readonly DiagnosticDescriptor MemoryMappedFileInSSTableReader = new(
        "JCC9310",
        "PithosDB 安全: SSTableReader 中禁止使用 MemoryMappedFile.CreateFromFile",
        "SSTableReader 中使用了 MemoryMappedFile.CreateFromFile — 会导致 Windows 文件锁冲突 (FileShare.None 独占锁). 应使用 byte[] + GCHandle.Pinned 替代, 彻底消除文件锁.",
        "ResourceSafety",
        DiagnosticSeverity.Error,
        true,
        "Root cause: MemoryMappedFile.CreateFromFile defaults to FileShare.None (exclusive lock), causing IOException when multiple processes/instances read the same SST file. " +
        "Fix: read the file into a byte[] via FileStream(FileShare.ReadWrite), pin with GCHandle.Alloc(buffer, GCHandleType.Pinned), and cast (byte*)handle.AddrOfPinnedObject() to get the pointer. " +
        "In Dispose, call _handle.Free() to release the GCHandle. No file lock, cross-process safe.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(MemoryMappedFileInSSTableReader);

    public override void Initialize(AnalysisContext context) {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
    }

    private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext ctx) {
        if (ctx.CancellationToken.IsCancellationRequested) return;
        if (ctx.Node is not MemberAccessExpressionSyntax memberAccess) return;

        var memberName = memberAccess.Name.Identifier.ValueText;
        if (memberName != "CreateFromFile" && memberName != "CreateViewAccessor" && memberName != "CreateViewStream") return;

        var typeInfo = ctx.SemanticModel.GetTypeInfo(memberAccess.Expression, ctx.CancellationToken);
        var type = typeInfo.Type;
        if (type is null) return;

        var typeName = type.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (!typeName.StartsWith("global::System.IO.MemoryMappedFiles.MemoryMappedFile", StringComparison.Ordinal)) return;

        var enclosingSymbol = ctx.SemanticModel.GetEnclosingSymbol(memberAccess.SpanStart, ctx.CancellationToken);
        if (enclosingSymbol is null) return;
        var containingType = enclosingSymbol.ContainingType;
        if (containingType is null) return;

        if (containingType.Name != "SSTableReader") return;

        ctx.ReportDiagnostic(Diagnostic.Create(MemoryMappedFileInSSTableReader, memberAccess.Name.GetLocation()));
    }
}
