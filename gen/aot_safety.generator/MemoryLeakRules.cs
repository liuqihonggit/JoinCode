namespace AotSafety.Generator {
    /// <summary>
    /// 内存泄漏检测分析器：检查所有 IDisposable/IAsyncDisposable 字段是否在 Dispose/DisposeAsync 中释放。
    /// JCC9301: IDisposable 字段未在 Dispose/DisposeAsync 中释放
    /// JCC9302: 可空 IDisposable 字段释放后未置 null
    /// JCC9304: base.Dispose() 不在 Dispose 方法体最后位置（释放顺序错误）
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class MemoryLeakRules : DiagnosticAnalyzer {
        private static readonly DiagnosticDescriptor RuleDisposableFieldNotReleased = new(
            "JCC9301",
            "内存泄漏: IDisposable字段未在Dispose/DisposeAsync中释放",
            "字段 '{0}' 类型实现了 IDisposable/IAsyncDisposable，但未在 Dispose/DisposeAsync 方法中释放。将导致资源泄漏。",
            "ResourceSafety",
            DiagnosticSeverity.Warning,
            true,
            "字段类型实现了 IDisposable/IAsyncDisposable, 必须在 Dispose/DisposeAsync 方法中释放. " +
            "正确做法: 1) 在 Dispose() 中调用 field.Dispose() 或 field.DisposeSafe(_logger); " +
            "2) 在 DisposeAsync() 中调用 await field.DisposeAsync() 或 await field.DisposeSafeAsync(_logger); " +
            "3) 可空字段释放后应置 null 防止 use-after-free.",
            customTags: WellKnownDiagnosticTags.CompilationEnd);

        private static readonly DiagnosticDescriptor RuleNullableFieldNotNulled = new(
            "JCC9302",
            "内存泄漏: 可空IDisposable字段释放后未置null",
            "可空字段 '{0}' 在Dispose/DisposeAsync中释放后未置null。置null可防止use-after-free并明确表达释放意图。",
            "ResourceSafety",
            DiagnosticSeverity.Warning,
            true,
            "可空IDisposable字段在Dispose中释放后应置null. " +
            "正确做法: field?.Dispose(); field = null; 或 await (field?.DisposeAsync() ?? ValueTask.CompletedTask); field = null;. " +
            "置null可: 1) 防止use-after-free; 2) 明确表达释放意图; 3) 帮助检测重复释放.",
            customTags: WellKnownDiagnosticTags.CompilationEnd);

        private static readonly DiagnosticDescriptor RuleBaseDisposeNotLast = new(
            "JCC9304",
            "释放顺序: base.Dispose() 必须在 Dispose 方法体最后位置",
            "base.{0}() 不在 Dispose 方法体最后位置，其后还有 {1} 条语句。子类资源应先释放，base.Dispose() 最后调用（父类做生命周期注销）。先释放父类会导致子类释放时访问已释放的父类资源。",
            "ResourceSafety",
            DiagnosticSeverity.Warning,
            true,
            "base.Dispose()/base.DisposeAsync() must be the last statement in Dispose/DisposeAsync method. " +
            "Child resources should be released first, then base.Dispose() for parent lifecycle cleanup. " +
            "If base.Dispose() is called before other statements, those statements may access already-released parent resources.",
            customTags: WellKnownDiagnosticTags.CompilationEnd);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(RuleDisposableFieldNotReleased, RuleNullableFieldNotNulled, RuleBaseDisposeNotLast);

        public override void Initialize(AnalysisContext context) {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterCompilationStartAction(AnalyzeDisposableFields);
            context.RegisterSyntaxNodeAction(AnalyzeDisposeOrder, SyntaxKind.MethodDeclaration);
        }

        /// <summary>
        /// 收集所有 IDisposable 字段，编译结束时检查是否在 Dispose 中释放
        /// </summary>
        private static void AnalyzeDisposableFields(CompilationStartAnalysisContext context) {
            var disposableFields = new ConcurrentDictionary<IFieldSymbol, Location>(SymbolEqualityComparer.Default);
            var idisposableType = context.Compilation.GetTypeByMetadataName("System.IDisposable");
            var iasyncDisposableType = context.Compilation.GetTypeByMetadataName("System.IAsyncDisposable");

            context.RegisterSyntaxNodeAction(ctx => {
                if (ctx.CancellationToken.IsCancellationRequested) return;

                var fieldDecl = (FieldDeclarationSyntax)ctx.Node;

                if (fieldDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword))) return;

                var fieldType = ctx.SemanticModel.GetTypeInfo(fieldDecl.Declaration.Type, ctx.CancellationToken).Type;
                if (fieldType is null) return;

                if (!IsDisposableType(fieldType, idisposableType, iasyncDisposableType)) return;

                if (IsExcludedType(fieldType)) return;

                foreach (var variable in fieldDecl.Declaration.Variables) {
                    if (variable.Initializer is null) continue;

                    if (AotSafetyHelpers.IsNullLiteral(variable.Initializer.Value)) continue;

                    var symbol = ctx.SemanticModel.GetDeclaredSymbol(variable, ctx.CancellationToken) as IFieldSymbol;
                    if (symbol is null) continue;

                    if (symbol.IsImplicitlyDeclared) continue;

                    if (symbol.IsStatic) continue;

                    disposableFields.TryAdd(symbol, variable.Identifier.GetLocation());
                }
            }, SyntaxKind.FieldDeclaration);

            context.RegisterCompilationEndAction(ctx => {
                if (ctx.CancellationToken.IsCancellationRequested) return;

                foreach (var kvp in disposableFields) {
                    if (ctx.CancellationToken.IsCancellationRequested) return;

                    var field = kvp.Key;
                    var location = kvp.Value;

                    var containingType = field.ContainingType;
                    if (containingType is null) continue;

                    if (!IsDisposableType(containingType, idisposableType, iasyncDisposableType)) continue;

                    var releasedInDispose = AotSafetyHelpers.CheckInDisposeCallChain(containingType,
                        m => AotSafetyHelpers.MethodBodyReferencesField(m, field.Name));

                    if (!releasedInDispose) {
                        ctx.ReportDiagnostic(Diagnostic.Create(RuleDisposableFieldNotReleased, location, field.Name));
                    } else if (field.NullableAnnotation == NullableAnnotation.Annotated && !field.IsReadOnly) {
                        var nulledInDispose = AotSafetyHelpers.CheckInDisposeCallChain(containingType,
                            m => AotSafetyHelpers.MethodBodyNullsField(m, field.Name));

                        if (!nulledInDispose) {
                            ctx.ReportDiagnostic(Diagnostic.Create(RuleNullableFieldNotNulled, location, field.Name));
                        }
                    }
                }
            });
        }

        /// <summary>
        /// 检测类型是否实现了 IDisposable 或 IAsyncDisposable
        /// </summary>
        private static bool IsDisposableType(ITypeSymbol type, INamedTypeSymbol? idisposable, INamedTypeSymbol? iasyncDisposable) {
            if (type is not INamedTypeSymbol namedType) return false;

            if (idisposable is not null && namedType.AllInterfaces.Contains(idisposable, SymbolEqualityComparer.Default))
                return true;

            if (iasyncDisposable is not null && namedType.AllInterfaces.Contains(iasyncDisposable, SymbolEqualityComparer.Default))
                return true;

            if (idisposable is not null && SymbolEqualityComparer.Default.Equals(namedType, idisposable))
                return true;

            if (iasyncDisposable is not null && SymbolEqualityComparer.Default.Equals(namedType, iasyncDisposable))
                return true;

            return false;
        }

        /// <summary>
        /// 排除由其他分析器处理的类型：SemaphoreSlim(JCC4005)、ConcurrentDictionary(JCC4006)
        /// </summary>
        private static bool IsExcludedType(ITypeSymbol type) {
            var nameSpan = type.Name.AsSpan();
            if (nameSpan.SequenceEqual("SemaphoreSlim".AsSpan())) return true;
            if (nameSpan.SequenceEqual("ConcurrentDictionary".AsSpan())) return true;
            return false;
        }

        /// <summary>
        /// JCC9304: 检测 base.Dispose()/base.DisposeAsync() 不在 Dispose 方法体最后位置。
        /// 释放顺序：子类资源先释放 → base.Dispose() 最后调用（父类做生命周期注销）。
        /// 检测方式：方法体中存在 base.Dispose() 调用，但最后一个语句不包含 base.Dispose() 调用。
        /// </summary>
        private static void AnalyzeDisposeOrder(SyntaxNodeAnalysisContext ctx) {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            var methodDecl = (MethodDeclarationSyntax)ctx.Node;

            var methodName = methodDecl.Identifier.ValueText.AsSpan();
            if (!methodName.SequenceEqual("Dispose".AsSpan()) &&
                !methodName.SequenceEqual("DisposeAsync".AsSpan()))
                return;

            if (!methodDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.OverrideKeyword)))
                return;

            if (methodDecl.Body is null) return;

            var statements = methodDecl.Body.Statements;
            if (statements.Count <= 1) return;

            var hasBaseDispose = methodDecl.Body.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Any(IsBaseDisposeCall);

            if (!hasBaseDispose) return;

            var lastStatement = statements[statements.Count - 1];
            var lastHasBaseDispose = lastStatement.DescendantNodesAndSelf()
                .OfType<InvocationExpressionSyntax>()
                .Any(IsBaseDisposeCall);

            if (!lastHasBaseDispose) {
                ctx.ReportDiagnostic(Diagnostic.Create(RuleBaseDisposeNotLast,
                    lastStatement.GetLocation(), "Dispose", 1));
            }
        }

        /// <summary>
        /// 检测调用是否是 base.Dispose() 或 base.DisposeAsync()
        /// </summary>
        private static bool IsBaseDisposeCall(InvocationExpressionSyntax invocation) {
            if (invocation.Expression is not MemberAccessExpressionSyntax ma)
                return false;

            if (ma.Expression is not BaseExpressionSyntax)
                return false;

            var name = ma.Name.Identifier.ValueText.AsSpan();
            return name.SequenceEqual("Dispose".AsSpan()) ||
                   name.SequenceEqual("DisposeAsync".AsSpan());
        }
    }
}