namespace AotSafety.Generator
{
    /// <summary>
    /// 可空容器规则（JCC11002）：可空容器字段/属性建议改为非空初始化。
    /// 例如 List of T 问号字段建议改为 List of T 等于 new，避免 null 检查。
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class NullableContainerRules : DiagnosticAnalyzer
    {
        private static readonly DiagnosticDescriptor RuleNullableContainer = new(
            "JCC11002",
            "可空容器: 可空容器字段/属性建议改为非空初始化",
            "可空容器 {0} '{1}' 建议改为非空初始化（{2} = [] 或 = new()）。空容器比 null 更健壮: 无需 null 检查, JSON 反序列化无对应字段时默认空集合而非 null。",
            "CodeStyle",
            DiagnosticSeverity.Warning,
            true,
            "可空容器字段导致大量 null 检查, 且 JSON 反序列化时可能为 null. 空容器（= []）是更合理的默认值: 1) 遍历空集合是 no-op, 无需 null 检查; 2) JSON 反序列化无字段时保持空集合; 3) 消除 NullReferenceException 风险. 例外: 延迟初始化（先 null 后赋值）、可选参数、可能无结果的返回值.");

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
            ImmutableArray.Create(RuleNullableContainer);

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
            var type = fieldDecl.Declaration.Type;
            if (!IsNullableContainerType(type, out var baseTypeName)) return;

            var typeDecl = fieldDecl.Parent as TypeDeclarationSyntax;
            if (ShouldSkipType(typeDecl)) return;

            foreach (var declarator in fieldDecl.Declaration.Variables)
            {
                var typeName = type.ToString();
                var fieldName = declarator.Identifier.ValueText;
                ctx.ReportDiagnostic(Diagnostic.Create(RuleNullableContainer, declarator.GetLocation(), typeName, fieldName, baseTypeName));
            }
        }

        private static void AnalyzePropertyDeclaration(SyntaxNodeAnalysisContext ctx)
        {
            if (ctx.CancellationToken.IsCancellationRequested) return;
            var propDecl = (PropertyDeclarationSyntax)ctx.Node;
            if (!IsAutoProperty(propDecl)) return;
            if (propDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.AbstractKeyword) || m.IsKind(SyntaxKind.OverrideKeyword) || m.IsKind(SyntaxKind.RequiredKeyword))) return;
            var type = propDecl.Type;
            if (!IsNullableContainerType(type, out var baseTypeName)) return;

            var typeDecl = propDecl.Parent as TypeDeclarationSyntax;
            if (ShouldSkipType(typeDecl)) return;

            var typeName = type.ToString();
            var propName = propDecl.Identifier.ValueText;
            ctx.ReportDiagnostic(Diagnostic.Create(RuleNullableContainer, propDecl.Identifier.GetLocation(), typeName, propName, baseTypeName));
        }

        private static bool IsNullableContainerType(TypeSyntax typeSyntax, out string baseTypeName)
        {
            baseTypeName = string.Empty;
            if (typeSyntax is not NullableTypeSyntax nullable) return false;
            var name = ExtractTypeName(nullable.ElementType);
            if (name is null || !ContainerTypeNames.Contains(name)) return false;
            baseTypeName = nullable.ElementType.ToString();
            return true;
        }

        private static bool ShouldSkipType(TypeDeclarationSyntax? typeDecl)
        {
            if (typeDecl is null) return true;
            var kind = typeDecl.Kind();
            return kind == SyntaxKind.InterfaceDeclaration
                || kind == SyntaxKind.RecordDeclaration
                || kind == SyntaxKind.RecordStructDeclaration;
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

        private static bool IsAutoProperty(PropertyDeclarationSyntax propDecl)
        {
            if (propDecl.AccessorList is null) return false;
            foreach (var accessor in propDecl.AccessorList.Accessors)
            {
                if (accessor.Body is not null || accessor.ExpressionBody is not null)
                    return false;
            }
            return true;
        }
    }
}
