namespace AotSafety.Generator {
    /// <summary>
    /// 非托管资源检测分析器：检测 IntPtr/UIntPtr 字段，建议用 SafeHandle 模式替代。
    /// JCC9303: 检测到非托管资源字段(IntPtr/UIntPtr)，建议用 SafeHandle 替代直接持有
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UnmanagedResourceRules : DiagnosticAnalyzer {
        private static readonly DiagnosticDescriptor RuleUnmanagedFieldWithoutSafeHandle = new(
            "JCC9303",
            "非托管资源: IntPtr/UIntPtr 字段应改用 SafeHandle 模式",
            "字段 '{0}' 是非托管资源类型({1})，直接持有非托管句柄不安全。应改用 SafeHandle 派生类封装，由运行时保证释放。",
            "ResourceSafety",
            DiagnosticSeverity.Warning,
            true,
            "Direct IntPtr/UIntPtr fields require manual finalizer and Dispose management, error-prone. " +
            "SafeHandle pattern: 1) Create a SafeHandle-derived class (e.g. SafeHandleMinusOneIsInvalid); " +
            "2) Override ReleaseHandle() to free the unmanaged resource; " +
            "3) Replace IntPtr field with SafeHandle field; " +
            "4) Runtime guarantees ReleaseHandle() is called exactly once, even on exceptions or AppDomain unload. " +
            "See Microsoft.Win32.SafeHandles namespace for built-in SafeHandle types.",
            customTags: WellKnownDiagnosticTags.CompilationEnd);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(RuleUnmanagedFieldWithoutSafeHandle);

        public override void Initialize(AnalysisContext context) {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterCompilationStartAction(AnalyzeUnmanagedFields);
        }

        /// <summary>
        /// 收集所有 IntPtr/UIntPtr 字段，编译结束时检查是否应该用 SafeHandle 替代
        /// </summary>
        private static void AnalyzeUnmanagedFields(CompilationStartAnalysisContext context) {
            var unmanagedFields = new ConcurrentDictionary<IFieldSymbol, (Location, string)>(SymbolEqualityComparer.Default);

            var safeHandleType = context.Compilation.GetTypeByMetadataName("System.Runtime.InteropServices.SafeHandle");
            var idisposableType = context.Compilation.GetTypeByMetadataName("System.IDisposable");
            var iasyncDisposableType = context.Compilation.GetTypeByMetadataName("System.IAsyncDisposable");

            context.RegisterSyntaxNodeAction(ctx => {
                if (ctx.CancellationToken.IsCancellationRequested) return;

                var fieldDecl = (FieldDeclarationSyntax)ctx.Node;

                if (fieldDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword))) return;

                var fieldType = ctx.SemanticModel.GetTypeInfo(fieldDecl.Declaration.Type, ctx.CancellationToken).Type;
                if (fieldType is null) return;

                if (!IsUnmanagedType(fieldType)) return;

                foreach (var variable in fieldDecl.Declaration.Variables) {
                    var symbol = ctx.SemanticModel.GetDeclaredSymbol(variable, ctx.CancellationToken) as IFieldSymbol;
                    if (symbol is null) continue;

                    if (symbol.IsImplicitlyDeclared) continue;

                    if (symbol.IsStatic) continue;

                    unmanagedFields.TryAdd(symbol, (variable.Identifier.GetLocation(), fieldType.Name));
                }
            }, SyntaxKind.FieldDeclaration);

            context.RegisterCompilationEndAction(ctx => {
                if (ctx.CancellationToken.IsCancellationRequested) return;

                foreach (var kvp in unmanagedFields) {
                    if (ctx.CancellationToken.IsCancellationRequested) return;

                    var field = kvp.Key;
                    var (location, typeName) = kvp.Value;

                    var containingType = field.ContainingType;
                    if (containingType is null) continue;

                    if (containingType.TypeKind != TypeKind.Class) continue;

                    if (!ImplementsDisposable(containingType, idisposableType, iasyncDisposableType)) continue;

                    if (safeHandleType is not null && IsOrInheritsFrom(containingType, safeHandleType))
                        continue;

                    if (AotSafetyHelpers.CheckInDisposeCallChain(containingType,
                            m => AotSafetyHelpers.MethodBodyReferencesField(m, field.Name)))
                        continue;

                    ctx.ReportDiagnostic(Diagnostic.Create(RuleUnmanagedFieldWithoutSafeHandle, location, field.Name, typeName));
                }
            });
        }

        /// <summary>
        /// 检测类型是否是非托管资源类型(IntPtr/UIntPtr/HGlobal/ComHandle)
        /// </summary>
        private static bool IsUnmanagedType(ITypeSymbol type) {
            if (type.SpecialType == SpecialType.System_IntPtr) return true;
            if (type.SpecialType == SpecialType.System_UIntPtr) return true;

            var nameSpan = type.Name.AsSpan();
            if (nameSpan.SequenceEqual("HandleRef".AsSpan())) return true;
            if (nameSpan.SequenceEqual("HGlobal".AsSpan())) return true;

            return false;
        }

        /// <summary>
        /// 检测类型是否是 SafeHandle 或其派生类
        /// </summary>
        private static bool IsOrInheritsFrom(INamedTypeSymbol type, INamedTypeSymbol baseType) {
            var current = type;
            while (current is not null) {
                if (SymbolEqualityComparer.Default.Equals(current, baseType))
                    return true;
                current = current.BaseType;
            }
            return false;
        }

        /// <summary>
        /// 检测类型是否实现了 IDisposable 或 IAsyncDisposable。
        /// 只对实现释放接口的类报告 JCC9303 — 不实现 IDisposable 的类中的 IntPtr 字段是值传递/借用句柄，非拥有的资源。
        /// </summary>
        private static bool ImplementsDisposable(INamedTypeSymbol type, INamedTypeSymbol? idisposable, INamedTypeSymbol? iasyncDisposable) {
            if (idisposable is not null && type.AllInterfaces.Contains(idisposable, SymbolEqualityComparer.Default))
                return true;

            if (iasyncDisposable is not null && type.AllInterfaces.Contains(iasyncDisposable, SymbolEqualityComparer.Default))
                return true;

            if (idisposable is not null && SymbolEqualityComparer.Default.Equals(type, idisposable))
                return true;

            if (iasyncDisposable is not null && SymbolEqualityComparer.Default.Equals(type, iasyncDisposable))
                return true;

            return false;
        }
    }
}