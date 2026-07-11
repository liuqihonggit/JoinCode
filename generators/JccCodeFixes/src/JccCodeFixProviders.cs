namespace JccCodeFixes;

/// <summary>
/// JCC1001/JCC1002 CodeFix: Dictionary&lt;string, object&gt; → Dictionary&lt;string, JsonElement&gt;
/// 将 AOT 不兼容的 object 值类型替换为 JsonElement
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(Jcc1001CodeFixProvider))]
public sealed class Jcc1001CodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create("JCC1001", "JCC1002", "JCC1003");

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics.FirstOrDefault(d =>
            d.Id == "JCC1001" || d.Id == "JCC1002" || d.Id == "JCC1003");
        if (diagnostic is null) return;

        var node = root.FindNode(diagnostic.Location.SourceSpan);

        context.RegisterCodeFix(
            CodeAction.Create(
                "替换为 Dictionary<string, JsonElement>",
                ct => ReplaceObjectWithJsonElement(context.Document, node, ct),
                nameof(Jcc1001CodeFixProvider)),
            diagnostic);
    }

    private static async Task<Document> ReplaceObjectWithJsonElement(
        Document document, SyntaxNode node, CancellationToken ct)
    {
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null) return document;

        // 查找所有 Dictionary<string, object> / Dictionary<string, object?> 类型节点
        var dictTypes = new List<GenericNameSyntax>();
        CollectDictionaryObjectTypes(node, dictTypes);

        if (dictTypes.Count == 0)
        {
            // 节点本身可能就是类型节点
            if (node is GenericNameSyntax generic && IsDictionaryStringObject(generic))
                dictTypes.Add(generic);
            else
                // 在节点范围内搜索
                foreach (var descendant in node.DescendantNodesAndSelf())
                    if (descendant is GenericNameSyntax gn && IsDictionaryStringObject(gn))
                        dictTypes.Add(gn);
        }

        if (dictTypes.Count == 0) return document;

        var newRoot = root.ReplaceNodes(
            dictTypes,
            (original, _) => ReplaceDictionaryObjectType(original));

        return document.WithSyntaxRoot(newRoot);
    }

    /// <summary>
    /// 收集节点中的所有 Dictionary&lt;string, object&gt; 类型引用
    /// </summary>
    private static void CollectDictionaryObjectTypes(SyntaxNode node, List<GenericNameSyntax> results)
    {
        foreach (var descendant in node.DescendantNodesAndSelf())
        {
            if (descendant is GenericNameSyntax generic && IsDictionaryStringObject(generic))
                results.Add(generic);
        }
    }

    /// <summary>
    /// 判断是否是 Dictionary&lt;string, object&gt; 或 Dictionary&lt;string, object?&gt;
    /// </summary>
    private static bool IsDictionaryStringObject(GenericNameSyntax generic)
    {
        if (generic.Identifier.ValueText != "Dictionary") return false;
        var args = generic.TypeArgumentList.Arguments;
        if (args.Count != 2) return false;

        // 第一个参数必须是 string
        var firstArg = args[0].ToString().Trim();
        if (firstArg != "string") return false;

        // 第二个参数必须是 object 或 object?
        var secondArg = args[1].ToString().Trim();
        return secondArg == "object" || secondArg == "object?";
    }

    /// <summary>
    /// 将 Dictionary&lt;string, object&gt; 替换为 Dictionary&lt;string, JsonElement&gt;
    /// </summary>
    private static GenericNameSyntax ReplaceDictionaryObjectType(GenericNameSyntax original)
    {
        var args = original.TypeArgumentList.Arguments;
        // 保留 string 参数，替换 object/object? 为 JsonElement
        var newSecondArg = SyntaxFactory.IdentifierName("JsonElement");
        var newTypeArgs = SyntaxFactory.SeparatedList<TypeSyntax>(new[]
        {
            args[0],
            newSecondArg
        });

        return original.WithTypeArgumentList(
            original.TypeArgumentList.WithArguments(newTypeArgs));
    }
}

/// <summary>
/// JCC6005 CodeFix: List.Insert(0, item) → list.Add(item) + 循环后 list.Reverse()
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(Jcc6005CodeFixProvider))]
public sealed class Jcc6005CodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create("JCC6005");

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics.FirstOrDefault(d => d.Id == "JCC6005");
        if (diagnostic is null) return;

        var node = root.FindNode(diagnostic.Location.SourceSpan);
        if (node is not InvocationExpressionSyntax invocation) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "替换为 Add + Reverse",
                ct => ReplaceInsertWithAddReverse(context.Document, invocation, ct),
                nameof(Jcc6005CodeFixProvider)),
            diagnostic);
    }

    private static async Task<Document> ReplaceInsertWithAddReverse(
        Document document, InvocationExpressionSyntax invocation, CancellationToken ct)
    {
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null) return document;

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) return document;
        if (memberAccess.Expression is null) return document;

        var receiver = memberAccess.Expression;
        var args = invocation.ArgumentList.Arguments;
        if (args.Count < 2) return document;

        var valueArg = args[1].Expression;

        // 构造: receiver.Add(valueArg)
        var addInvocation = SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                receiver.WithoutTrivia(),
                SyntaxFactory.IdentifierName("Add")),
            SyntaxFactory.ArgumentList(
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.Argument(valueArg))));

        var newInvocation = addInvocation
            .WithLeadingTrivia(invocation.GetLeadingTrivia())
            .WithTrailingTrivia(invocation.GetTrailingTrivia());

        var newRoot = root.ReplaceNode(invocation, newInvocation);

        // 在包含循环的 Block 末尾添加 receiver.Reverse() 语句
        var loopNode = FindEnclosingLoop(invocation);
        if (loopNode is null) return document.WithSyntaxRoot(newRoot);

        var currentRoot = newRoot;
        var loopInNewRoot = currentRoot.FindNode(loopNode.Span);
        var parentBlock = loopInNewRoot.Parent as BlockSyntax;
        if (parentBlock is null) return document.WithSyntaxRoot(currentRoot);

        // 提取循环语句的缩进
        var loopStatement = loopInNewRoot as StatementSyntax;
        var indentation = CodeFixIndentationHelper.GetIndentation(loopStatement);

        // 构造: receiver.Reverse();  带正确缩进
        var reverseStatement = SyntaxFactory.ExpressionStatement(
            SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    receiver.WithoutTrivia(),
                    SyntaxFactory.IdentifierName("Reverse")),
                SyntaxFactory.ArgumentList()))
            .WithLeadingTrivia(SyntaxFactory.CarriageReturnLineFeed, indentation)
            .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);

        var newStatements = parentBlock.Statements.ToList();
        var loopIndex = newStatements.FindIndex(s => s.SpanStart >= loopInNewRoot.SpanStart);
        if (loopIndex >= 0)
        {
            newStatements.Insert(loopIndex + 1, reverseStatement);
        }
        else
        {
            newStatements.Add(reverseStatement);
        }

        var newBlock = parentBlock.WithStatements(SyntaxFactory.List(newStatements));
        var finalRoot = currentRoot.ReplaceNode(parentBlock, newBlock);
        return document.WithSyntaxRoot(finalRoot);
    }

    private static SyntaxNode? FindEnclosingLoop(SyntaxNode node)
    {
        var current = node.Parent;
        while (current is not null)
        {
            if (current is ForEachStatementSyntax or ForStatementSyntax or WhileStatementSyntax or DoStatementSyntax)
                return current;
            current = current.Parent;
        }
        return null;
    }
}

/// <summary>
/// JCC6002 CodeFix: 循环内 List.Contains → 用 HashSet 替代
/// 在循环前添加 var set = new HashSet&lt;T&gt;(list); 将循环内的 list.Contains → set.Contains
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(Jcc6002CodeFixProvider))]
public sealed class Jcc6002CodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create("JCC6002");

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics.FirstOrDefault(d => d.Id == "JCC6002");
        if (diagnostic is null) return;

        var node = root.FindNode(diagnostic.Location.SourceSpan);
        if (node is not InvocationExpressionSyntax invocation) return;
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) return;
        if (memberAccess.Expression is null) return;

        var methodName = memberAccess.Name.Identifier.ValueText;
        if (methodName != "Contains") return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "用 HashSet 替代线性查找",
                ct => ReplaceWithHashSet(context.Document, invocation, memberAccess, ct),
                nameof(Jcc6002CodeFixProvider)),
            diagnostic);
    }

    private static async Task<Document> ReplaceWithHashSet(
        Document document, InvocationExpressionSyntax invocation,
        MemberAccessExpressionSyntax memberAccess, CancellationToken ct)
    {
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null) return document;

        var receiver = memberAccess.Expression;
        var receiverText = receiver.ToString();

        var semanticModel = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
        if (semanticModel is null) return document;

        var receiverSymbol = semanticModel.GetSymbolInfo(receiver, ct).Symbol;
        if (receiverSymbol is null) return document;

        var elementType = GetElementType(receiverSymbol);
        if (elementType is null) return document;

        var elementTypeText = elementType.ToDisplayString(
            SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(
                SymbolDisplayGlobalNamespaceStyle.Omitted));

        var setName = $"{receiverText}Set";

        // 构造: var listSet = new HashSet<T>(list);
        var hashSetType = SyntaxFactory.GenericName(
            SyntaxFactory.Identifier("HashSet"))
            .WithTypeArgumentList(
                SyntaxFactory.TypeArgumentList(
                    SyntaxFactory.SingletonSeparatedList<TypeSyntax>(
                        SyntaxFactory.IdentifierName(elementTypeText))));

        var hashSetCtorArgs = SyntaxFactory.ArgumentList(
            SyntaxFactory.SingletonSeparatedList(
                SyntaxFactory.Argument(receiver.WithoutTrivia())));

        var hashSetCreation = SyntaxFactory.ObjectCreationExpression(hashSetType)
            .WithArgumentList(hashSetCtorArgs);

        var setInitializer = SyntaxFactory.EqualsValueClause(hashSetCreation);

        var setDeclarator = SyntaxFactory.VariableDeclarator(setName)
            .WithInitializer(setInitializer);

        var setDeclaration = SyntaxFactory.LocalDeclarationStatement(
            SyntaxFactory.VariableDeclaration(
                SyntaxFactory.IdentifierName("var"),
                SyntaxFactory.SingletonSeparatedList(setDeclarator)));

        // 替换 list.Contains → set.Contains
        var newMemberAccess = memberAccess.WithExpression(SyntaxFactory.IdentifierName(setName));
        var newInvocation = invocation
            .WithExpression(newMemberAccess)
            .WithLeadingTrivia(invocation.GetLeadingTrivia())
            .WithTrailingTrivia(invocation.GetTrailingTrivia());

        var newRoot = root.ReplaceNode(invocation, newInvocation);

        var loopNode = FindEnclosingLoop(invocation);
        if (loopNode is null) return document.WithSyntaxRoot(newRoot);

        var loopInNewRoot = newRoot.FindNode(loopNode.Span);
        var parentBlock = loopInNewRoot.Parent as BlockSyntax;
        if (parentBlock is null) return document.WithSyntaxRoot(newRoot);

        var existingDecl = parentBlock.Statements
            .OfType<LocalDeclarationStatementSyntax>()
            .Any(s => s.Declaration.Variables.Any(v => v.Identifier.ValueText == setName));
        if (existingDecl) return document.WithSyntaxRoot(newRoot);

        // 提取循环语句的缩进
        var loopStatement = loopInNewRoot as StatementSyntax;
        var indentation = CodeFixIndentationHelper.GetIndentation(loopStatement);

        // 在循环语句前插入 HashSet 声明，带正确缩进
        var newStatements = parentBlock.Statements.ToList();
        var loopIndex = newStatements.FindIndex(s => s.SpanStart >= loopInNewRoot.SpanStart);
        if (loopIndex >= 0)
        {
            var declWithTrivia = setDeclaration
                .WithLeadingTrivia(indentation)
                .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);
            newStatements.Insert(loopIndex, declWithTrivia);
        }

        var newBlock = parentBlock.WithStatements(SyntaxFactory.List(newStatements));
        var finalRoot = newRoot.ReplaceNode(parentBlock, newBlock);
        return document.WithSyntaxRoot(finalRoot);
    }

    private static ITypeSymbol? GetElementType(ISymbol symbol)
    {
        var type = symbol switch
        {
            ILocalSymbol local => local.Type,
            IFieldSymbol field => field.Type,
            IParameterSymbol param => param.Type,
            IPropertySymbol prop => prop.Type,
            _ => null,
        };

        if (type is null) return null;

        if (type is INamedTypeSymbol namedType && namedType.IsGenericType)
        {
            var typeArgs = namedType.TypeArguments;
            if (typeArgs.Length > 0)
                return typeArgs[0];
        }

        return null;
    }

    private static SyntaxNode? FindEnclosingLoop(SyntaxNode node)
    {
        var current = node.Parent;
        while (current is not null)
        {
            if (current is ForEachStatementSyntax or ForStatementSyntax or WhileStatementSyntax or DoStatementSyntax)
                return current;
            current = current.Parent;
        }
        return null;
    }
}

/// <summary>
/// CodeFixProvider 共享的缩进提取工具
/// </summary>
internal static class CodeFixIndentationHelper
{
    /// <summary>
    /// 从语句的 leading trivia 中提取缩进（空格/制表符序列）
    /// </summary>
    public static SyntaxTrivia GetIndentation(StatementSyntax? statement)
    {
        if (statement is null)
            return SyntaxFactory.Whitespace("    ");

        var leadingTrivia = statement.GetLeadingTrivia();
        foreach (var trivia in leadingTrivia.Reverse())
        {
            if (trivia.IsKind(SyntaxKind.WhitespaceTrivia))
                return trivia;
        }

        return SyntaxFactory.Whitespace("    ");
    }
}

/// <summary>
/// JCC4005 CodeFix: SemaphoreSlim 字段未在 Dispose 中释放 → 自动添加 Dispose 调用
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(Jcc4005CodeFixProvider))]
public sealed class Jcc4005CodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create("JCC4005");

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics.FirstOrDefault(d => d.Id == "JCC4005");
        if (diagnostic is null) return;

        // 从诊断消息中提取字段名: "SemaphoreSlim 字段 '{0}' 未在 Dispose..."
        var fieldName = diagnostic.Properties.TryGetValue("FieldName", out var name)
            ? name
            : ExtractFieldNameFromMessage(diagnostic.GetMessage());

        if (fieldName is null) return;

        var node = root.FindNode(diagnostic.Location.SourceSpan);

        context.RegisterCodeFix(
            CodeAction.Create(
                $"在 Dispose 中添加 {fieldName}.Dispose()",
                ct => AddDisposeCall(context.Document, node, fieldName, ct),
                nameof(Jcc4005CodeFixProvider)),
            diagnostic);
    }

    private static async Task<Document> AddDisposeCall(
        Document document, SyntaxNode fieldNode, string fieldName, CancellationToken ct)
    {
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null) return document;

        // 找到包含此字段的类型声明
        var typeDecl = FindEnclosingTypeDeclaration(fieldNode);
        if (typeDecl is null) return document;

        // 查找 Dispose 方法
        var disposeMethod = typeDecl.Members
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Identifier.ValueText == "Dispose");

        if (disposeMethod is null) return document;

        var newRoot = root;

        if (disposeMethod.Body is not null)
        {
            // 块体方法: 在方法体末尾添加 _field.Dispose();
            var disposeStatement = CreateDisposeStatement(fieldName, disposeMethod);

            var newStatements = disposeMethod.Body.Statements.Add(disposeStatement);
            var newBody = disposeMethod.Body.WithStatements(newStatements);
            var newDisposeMethod = disposeMethod.WithBody(newBody);
            newRoot = root.ReplaceNode(disposeMethod, newDisposeMethod);
        }
        else if (disposeMethod.ExpressionBody is not null)
        {
            // 表达式体方法: 转换为块体并添加 Dispose 调用
            var existingExpr = disposeMethod.ExpressionBody.Expression;

            var existingStatement = SyntaxFactory.ExpressionStatement(existingExpr)
                .WithLeadingTrivia(SyntaxFactory.Whitespace("    "))
                .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);

            var disposeStatement = CreateDisposeStatement(fieldName, disposeMethod);

            var block = SyntaxFactory.Block(existingStatement, disposeStatement);
            var newDisposeMethod = disposeMethod
                .WithBody(block)
                .WithExpressionBody(null)
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.None));

            newRoot = root.ReplaceNode(disposeMethod, newDisposeMethod);
        }

        return document.WithSyntaxRoot(newRoot);
    }

    private static StatementSyntax CreateDisposeStatement(string fieldName, MethodDeclarationSyntax disposeMethod)
    {
        // 从方法体的第一条语句获取缩进，如果没有则用默认缩进
        var indentation = disposeMethod.Body?.Statements.FirstOrDefault() is { } firstStmt
            ? CodeFixIndentationHelper.GetIndentation(firstStmt)
            : SyntaxFactory.Whitespace("        ");

        return SyntaxFactory.ExpressionStatement(
            SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.IdentifierName(fieldName),
                    SyntaxFactory.IdentifierName("Dispose")),
                SyntaxFactory.ArgumentList()))
            .WithLeadingTrivia(indentation)
            .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);
    }

    /// <summary>
    /// 从诊断消息中提取字段名（格式: "SemaphoreSlim 字段 'xxx' 未在 Dispose..."）
    /// </summary>
    private static string? ExtractFieldNameFromMessage(string message)
    {
        var start = message.IndexOf('\'');
        if (start < 0) return null;
        var end = message.IndexOf('\'', start + 1);
        if (end < 0) return null;
        return message.Substring(start + 1, end - start - 1);
    }

    private static TypeDeclarationSyntax? FindEnclosingTypeDeclaration(SyntaxNode node)
    {
        var current = node.Parent;
        while (current is not null)
        {
            if (current is TypeDeclarationSyntax typeDecl)
                return typeDecl;
            current = current.Parent;
        }
        return null;
    }
}

/// <summary>
/// JCC4006 CodeFix: ConcurrentDictionary&lt;SemaphoreSlim&gt; 未逐个 Dispose → 添加 foreach Dispose + Clear
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(Jcc4006CodeFixProvider))]
public sealed class Jcc4006CodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create("JCC4006");

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics.FirstOrDefault(d => d.Id == "JCC4006");
        if (diagnostic is null) return;

        var fieldName = ExtractFieldNameFromMessage(diagnostic.GetMessage());
        if (fieldName is null) return;

        var node = root.FindNode(diagnostic.Location.SourceSpan);

        context.RegisterCodeFix(
            CodeAction.Create(
                $"在 Dispose 中添加 foreach 逐个 Dispose {fieldName}",
                ct => AddForEachDispose(context.Document, node, fieldName, ct),
                nameof(Jcc4006CodeFixProvider)),
            diagnostic);
    }

    private static async Task<Document> AddForEachDispose(
        Document document, SyntaxNode fieldNode, string fieldName, CancellationToken ct)
    {
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null) return document;

        var typeDecl = FindEnclosingTypeDeclaration(fieldNode);
        if (typeDecl is null) return document;

        var disposeMethod = typeDecl.Members
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Identifier.ValueText == "Dispose");

        if (disposeMethod is null) return document;

        var newRoot = root;

        // 构造: foreach (var kvp in _field) { kvp.Value.Dispose(); }
        var forEachStatement = CreateForEachDisposeStatement(fieldName, disposeMethod);

        // 构造: _field.Clear();
        var clearStatement = CreateClearStatement(fieldName, disposeMethod);

        if (disposeMethod.Body is not null)
        {
            var newStatements = disposeMethod.Body.Statements
                .Add(forEachStatement)
                .Add(clearStatement);
            var newBody = disposeMethod.Body.WithStatements(newStatements);
            var newDisposeMethod = disposeMethod.WithBody(newBody);
            newRoot = root.ReplaceNode(disposeMethod, newDisposeMethod);
        }
        else if (disposeMethod.ExpressionBody is not null)
        {
            var existingExpr = disposeMethod.ExpressionBody.Expression;
            var existingStatement = SyntaxFactory.ExpressionStatement(existingExpr)
                .WithLeadingTrivia(SyntaxFactory.Whitespace("    "))
                .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);

            var block = SyntaxFactory.Block(existingStatement, forEachStatement, clearStatement);
            var newDisposeMethod = disposeMethod
                .WithBody(block)
                .WithExpressionBody(null)
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.None));

            newRoot = root.ReplaceNode(disposeMethod, newDisposeMethod);
        }

        return document.WithSyntaxRoot(newRoot);
    }

    private static ForEachStatementSyntax CreateForEachDisposeStatement(string fieldName, MethodDeclarationSyntax disposeMethod)
    {
        var indentation = disposeMethod.Body?.Statements.FirstOrDefault() is { } firstStmt
            ? CodeFixIndentationHelper.GetIndentation(firstStmt)
            : SyntaxFactory.Whitespace("        ");
        var innerIndent = SyntaxFactory.Whitespace(indentation.ToString() + "    ");

        // kvp.Value.Dispose();
        var disposeInvocation = SyntaxFactory.ExpressionStatement(
            SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.IdentifierName("kvp"),
                        SyntaxFactory.IdentifierName("Value")),
                    SyntaxFactory.IdentifierName("Dispose")),
                SyntaxFactory.ArgumentList()))
            .WithLeadingTrivia(innerIndent)
            .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);

        var block = SyntaxFactory.Block(disposeInvocation);

        // foreach (var kvp in _field) { ... }
        return SyntaxFactory.ForEachStatement(
            SyntaxFactory.IdentifierName("var"),
            SyntaxFactory.Identifier("kvp"),
            SyntaxFactory.IdentifierName(fieldName),
            block)
            .WithLeadingTrivia(indentation)
            .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);
    }

    private static StatementSyntax CreateClearStatement(string fieldName, MethodDeclarationSyntax disposeMethod)
    {
        var indentation = disposeMethod.Body?.Statements.FirstOrDefault() is { } firstStmt
            ? CodeFixIndentationHelper.GetIndentation(firstStmt)
            : SyntaxFactory.Whitespace("        ");

        return SyntaxFactory.ExpressionStatement(
            SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.IdentifierName(fieldName),
                    SyntaxFactory.IdentifierName("Clear")),
                SyntaxFactory.ArgumentList()))
            .WithLeadingTrivia(indentation)
            .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);
    }

    private static string? ExtractFieldNameFromMessage(string message)
    {
        var start = message.IndexOf('\'');
        if (start < 0) return null;
        var end = message.IndexOf('\'', start + 1);
        if (end < 0) return null;
        return message.Substring(start + 1, end - start - 1);
    }

    private static TypeDeclarationSyntax? FindEnclosingTypeDeclaration(SyntaxNode node)
    {
        var current = node.Parent;
        while (current is not null)
        {
            if (current is TypeDeclarationSyntax typeDecl)
                return typeDecl;
            current = current.Parent;
        }
        return null;
    }
}

/// <summary>
/// JCC3009 CodeFix: 测试代码移除 ConfigureAwait(false)
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(Jcc3009CodeFixProvider))]
public sealed class Jcc3009CodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create("JCC3009");

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics.FirstOrDefault(d => d.Id == "JCC3009");
        if (diagnostic is null) return;

        var node = root.FindNode(diagnostic.Location.SourceSpan);

        context.RegisterCodeFix(
            CodeAction.Create(
                "移除 .ConfigureAwait(false)",
                ct => RemoveConfigureAwaitFalse(context.Document, node, ct),
                nameof(Jcc3009CodeFixProvider)),
            diagnostic);
    }

    private static async Task<Document> RemoveConfigureAwaitFalse(
        Document document, SyntaxNode node, CancellationToken ct)
    {
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null) return document;

        var awaitExpr = node.FirstAncestorOrSelf<AwaitExpressionSyntax>();
        if (awaitExpr is null) return document;

        if (awaitExpr.Expression is InvocationExpressionSyntax invocation &&
            invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Name.Identifier.ValueText == "ConfigureAwait")
        {
            var awaitedExpression = memberAccess.Expression;
            var newAwaitExpr = awaitExpr.WithExpression(awaitedExpression);
            var newRoot = root.ReplaceNode(awaitExpr, newAwaitExpr);
            return document.WithSyntaxRoot(newRoot);
        }

        return document;
    }
}
