namespace AotSafety.Generator
{
    /// <summary>
    /// 容器初始化规则（JCC11001）：容器类型字段/属性必须初始化，禁止为 null。
    /// 可空容器（如 List of T 问号）允许，用于延迟初始化场景。
    /// 构造函数中赋值的字段/属性自动豁免（同文件构造函数）。
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ContainerInitializationRules : DiagnosticAnalyzer
    {
        private static readonly DiagnosticDescriptor RuleContainerNotInitialized = new(
            "JCC11001",
            "容器初始化: 容器类型字段/属性必须初始化，禁止为 null",
            "容器类型 {0} '{1}' 未初始化，默认为 null。应在声明时初始化（如 = new()），或显式标记为可空。未初始化的容器在使用时会导致 NullReferenceException。",
            "CodeStyle",
            DiagnosticSeverity.Info,
            true,
            "容器类型（List/Dictionary/HashSet/数组等）未初始化时默认为 null, 使用时抛 NullReferenceException. 合规写法: 1) 声明时初始化; 2) 显式标记可空; 3) 构造函数赋值（已自动识别豁免）. 可空容器允许, 用于延迟初始化场景.");

        private static readonly HashSet<string> ContainerTypeNames = new(StringComparer.Ordinal)
        {
            "List", "Dictionary", "HashSet", "SortedList", "SortedDictionary", "SortedSet",
            "Stack", "Queue", "LinkedList", "Collection",
            "ReadOnlyCollection", "ReadOnlyDictionary", "ReadOnlyList",
            "ImmutableList", "ImmutableArray", "ImmutableHashSet", "ImmutableDictionary",
            "ImmutableSortedSet", "ImmutableSortedDictionary", "ImmutableQueue", "ImmutableStack",
            "FrozenSet", "FrozenDictionary", "FrozenList",
            "ConcurrentBag", "ConcurrentDictionary", "ConcurrentQueue", "ConcurrentStack",
            "IEnumerable", "IEnumerator", "ICollection", "IList", "IDictionary", "ISet",
            "ILookup", "IReadOnlyCollection", "IReadOnlyDictionary", "IReadOnlyList", "IReadOnlySet",
            "Array",
        };

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(RuleContainerNotInitialized);

        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterSyntaxNodeAction(AnalyzeFieldDeclaration, SyntaxKind.FieldDeclaration);
            context.RegisterSyntaxNodeAction(AnalyzePropertyDeclaration, SyntaxKind.PropertyDeclaration);
        }

        private static void AnalyzeFieldDeclaration(SyntaxNodeAnalysisContext ctx)
        {
            if (ctx.CancellationToken.IsCancellationRequested) return;
            var fieldDecl = (FieldDeclarationSyntax)ctx.Node;
            if (fieldDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.ConstKeyword))) return;
            if (!IsContainerType(fieldDecl.Declaration.Type)) return;

            var typeDecl = fieldDecl.Parent as TypeDeclarationSyntax;
            if (ShouldSkipType(typeDecl)) return;
            var assignedSymbols = CollectAssignedSymbols(typeDecl!, ctx);

            foreach (var declarator in fieldDecl.Declaration.Variables)
            {
                if (declarator.Initializer is not null) continue;

                var fieldSymbol = ctx.SemanticModel.GetDeclaredSymbol(declarator, ctx.CancellationToken);
                if (fieldSymbol is not null && assignedSymbols is not null && assignedSymbols.Contains(fieldSymbol))
                    continue;

                var typeName = fieldDecl.Declaration.Type.ToString();
                var fieldName = declarator.Identifier.ValueText;
                ctx.ReportDiagnostic(Diagnostic.Create(RuleContainerNotInitialized, declarator.GetLocation(), typeName, fieldName));
            }
        }

        private static void AnalyzePropertyDeclaration(SyntaxNodeAnalysisContext ctx)
        {
            if (ctx.CancellationToken.IsCancellationRequested) return;
            var propDecl = (PropertyDeclarationSyntax)ctx.Node;
            if (propDecl.Initializer is not null) return;
            if (!IsSettableAutoProperty(propDecl)) return;
            if (propDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.AbstractKeyword) || m.IsKind(SyntaxKind.OverrideKeyword) || m.IsKind(SyntaxKind.RequiredKeyword))) return;
            if (!IsContainerType(propDecl.Type)) return;

            var typeDecl = propDecl.Parent as TypeDeclarationSyntax;
            if (ShouldSkipType(typeDecl)) return;
            var assignedSymbols = CollectAssignedSymbols(typeDecl!, ctx);

            var propSymbol = ctx.SemanticModel.GetDeclaredSymbol(propDecl, ctx.CancellationToken);
            if (propSymbol is not null && assignedSymbols is not null && assignedSymbols.Contains(propSymbol))
                return;

            var typeName = propDecl.Type.ToString();
            var propName = propDecl.Identifier.ValueText;
            ctx.ReportDiagnostic(Diagnostic.Create(RuleContainerNotInitialized, propDecl.Identifier.GetLocation(), typeName, propName));
        }

        private static HashSet<ISymbol> CollectAssignedSymbols(TypeDeclarationSyntax typeDecl, SyntaxNodeAnalysisContext ctx)
        {
            var result = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            foreach (var ctor in typeDecl.Members.OfType<ConstructorDeclarationSyntax>())
            {
                var body = (SyntaxNode?)ctor.Body ?? ctor.ExpressionBody;
                if (body is null) continue;
                foreach (var assignment in body.DescendantNodes().OfType<AssignmentExpressionSyntax>())
                {
                    if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)) continue;
                    var sym = ctx.SemanticModel.GetSymbolInfo(assignment.Left, ctx.CancellationToken).Symbol;
                    if (sym is not null)
                        result.Add(sym);
                }
            }
            return result;
        }

        private static bool IsContainerType(TypeSyntax typeSyntax)
        {
            if (typeSyntax is NullableTypeSyntax) return false;
            if (typeSyntax is ArrayTypeSyntax) return true;

            var name = ExtractTypeName(typeSyntax);
            return name is not null && ContainerTypeNames.Contains(name);
        }

        private static string? ExtractTypeName(TypeSyntax typeSyntax)
        {
            return typeSyntax switch
            {
                GenericNameSyntax generic => generic.Identifier.ValueText,
                IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                QualifiedNameSyntax qualified => ExtractTypeName(qualified.Right),
                AliasQualifiedNameSyntax alias => ExtractTypeName(alias.Name),
                _ => null,
            };
        }

        private static bool ShouldSkipType(TypeDeclarationSyntax? typeDecl)
        {
            if (typeDecl is null) return true;
            var kind = typeDecl.Kind();
            return kind == SyntaxKind.InterfaceDeclaration
                || kind == SyntaxKind.RecordDeclaration
                || kind == SyntaxKind.RecordStructDeclaration;
        }

        private static bool IsSettableAutoProperty(PropertyDeclarationSyntax propDecl)
        {
            if (propDecl.AccessorList is null) return false;
            var hasSetter = false;
            foreach (var accessor in propDecl.AccessorList.Accessors)
            {
                if (accessor.Body is not null || accessor.ExpressionBody is not null)
                    return false;
                if (accessor.IsKind(SyntaxKind.SetAccessorDeclaration))
                    hasSetter = true;
            }
            return hasSetter;
        }
    }
}
