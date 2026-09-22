namespace CodeFixes;

/// <summary>
/// JCC9104/JCC9107 双向 CodeFix: 同步⇄异步 Dispose
/// <para>正向: using var x → await using var x, x.Dispose() → await x.DisposeAsync().ConfigureAwait(false)</para>
/// <para>反向: 当方法不能 async 时, 用 SyncFileReader 封装阻塞调用</para>
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DisposableConsistencyCodeFixProvider))]
public sealed class DisposableConsistencyCodeFixProvider : CodeFixProvider {
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create("JCC9104", "JCC9107");

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context) {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics.FirstOrDefault(d => d.Id == "JCC9104" || d.Id == "JCC9107");
        if (diagnostic is null) return;

        var node = root.FindNode(diagnostic.Location.SourceSpan);

        if (diagnostic.Id == "JCC9104") {
            context.RegisterCodeFix(
                CodeAction.Create(
                    "改为 await using",
                    ct => FixUsingToAwaitUsing(context.Document, node, ct),
                    nameof(DisposableConsistencyCodeFixProvider)),
                diagnostic);
        }

        if (diagnostic.Id == "JCC9107") {
            context.RegisterCodeFix(
                CodeAction.Create(
                    "改为 await DisposeAsync().ConfigureAwait(false)",
                    ct => FixDisposeToDisposeAsync(context.Document, node, ct),
                    nameof(DisposableConsistencyCodeFixProvider)),
                diagnostic);

            context.RegisterCodeFix(
                CodeAction.Create(
                    "用 SyncFileReader 封装同步调用",
                    ct => FixDisposeToSyncWrapper(context.Document, node, ct),
                    nameof(DisposableConsistencyCodeFixProvider) + "_Sync"),
                diagnostic);
        }
    }

    /// <summary>JCC9104 修复: using var x → await using var x</summary>
    private static async Task<Document> FixUsingToAwaitUsing(
        Document document, SyntaxNode node, CancellationToken ct) {
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null) return document;

        if (node is not LocalDeclarationStatementSyntax localDecl) return document;

        var awaitKeyword = SyntaxFactory.Token(SyntaxKind.AwaitKeyword);
        var newLocal = localDecl.WithUsingKeyword(
            localDecl.UsingKeyword.WithLeadingTrivia())
            .WithAwaitKeyword(awaitKeyword.WithTrailingTrivia(SyntaxFactory.Space));

        return document.WithSyntaxRoot(root.ReplaceNode(localDecl, newLocal));
    }

    /// <summary>JCC9107 正向修复: x.Dispose() → await x.DisposeAsync().ConfigureAwait(false)</summary>
    private static async Task<Document> FixDisposeToDisposeAsync(
        Document document, SyntaxNode node, CancellationToken ct) {
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null) return document;

        if (node is not InvocationExpressionSyntax invocation) return document;
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) return document;

        var receiver = memberAccess.Expression;
        var disposeAsyncMember = SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            receiver,
            SyntaxFactory.IdentifierName("DisposeAsync"));

        var disposeAsyncInvoke = SyntaxFactory.InvocationExpression(disposeAsyncMember);
        var configureAwaitMember = SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            disposeAsyncInvoke,
            SyntaxFactory.IdentifierName("ConfigureAwait"));
        var configureAwaitInvoke = SyntaxFactory.InvocationExpression(
            configureAwaitMember,
            SyntaxFactory.ArgumentList(
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.Argument(
                        SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression)))));

        var awaitExpr = SyntaxFactory.AwaitExpression(configureAwaitInvoke);

        return document.WithSyntaxRoot(root.ReplaceNode(invocation, awaitExpr));
    }

    /// <summary>JCC9107 反向修复: 用 SyncFileReader 封装同步调用</summary>
    private static async Task<Document> FixDisposeToSyncWrapper(
        Document document, SyntaxNode node, CancellationToken ct) {
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null) return document;

        if (node is not InvocationExpressionSyntax invocation) return document;
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) return document;

        var receiver = memberAccess.Expression;
        var disposeAsyncMember = SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            receiver,
            SyntaxFactory.IdentifierName("DisposeAsync"));

        var disposeAsyncInvoke = SyntaxFactory.InvocationExpression(disposeAsyncMember);
        var getAwaiterMember = SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            disposeAsyncInvoke,
            SyntaxFactory.IdentifierName("GetAwaiter"));
        var getAwaiterInvoke = SyntaxFactory.InvocationExpression(getAwaiterMember);
        var getResultMember = SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            getAwaiterInvoke,
            SyntaxFactory.IdentifierName("GetResult"));
        var getResultInvoke = SyntaxFactory.InvocationExpression(getResultMember);

        return document.WithSyntaxRoot(root.ReplaceNode(invocation, getResultInvoke));
    }
}
