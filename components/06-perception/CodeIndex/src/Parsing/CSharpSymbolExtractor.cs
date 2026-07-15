namespace JoinCode.CodeIndex.Ast;

public sealed class CSharpSymbolExtractor : ILanguagePlugin, IDisposable
{
    private static readonly FrozenDictionary<string, SymbolKind> NodeTypeToKind = new Dictionary<string, SymbolKind>()
    {
        ["class_declaration"] = SymbolKind.Class,
        ["struct_declaration"] = SymbolKind.Struct,
        ["interface_declaration"] = SymbolKind.Interface,
        ["enum_declaration"] = SymbolKind.Enum,
        ["method_declaration"] = SymbolKind.Method,
        ["property_declaration"] = SymbolKind.Property,
        ["event_declaration"] = SymbolKind.Event,
        ["delegate_declaration"] = SymbolKind.Delegate,
        ["namespace_declaration"] = SymbolKind.Namespace,
        ["file_scoped_namespace_declaration"] = SymbolKind.Namespace,
        ["constructor_declaration"] = SymbolKind.Constructor,
        ["record_declaration"] = SymbolKind.Record,
        ["operator_declaration"] = SymbolKind.Operator,
        ["indexer_declaration"] = SymbolKind.Indexer,
        ["destructor_declaration"] = SymbolKind.Destructor,
        ["local_function_statement"] = SymbolKind.LocalFunction
    }.ToFrozenDictionary();

    private static readonly FrozenSet<string> AccessibilityModifiers = FrozenSet.Create(
        StringComparer.Ordinal,
        "public", "private", "protected", "internal",
        "private_protected", "protected_internal");

    public string LanguageId => "c-sharp";

    public IReadOnlyList<string> FileExtensions => [".cs"];

    private readonly CSharpCallExtractor _callExtractor = new();
    private readonly CSharpDependencyExtractor _dependencyExtractor = new();
    private readonly TreeCache _treeCache;
    private readonly Threading.TimeoutLock _parseLock;
    private readonly TreeSitterParser? _dedicatedParser;
    private int _disposed;

    private static void Log(string message)
    {
        System.Diagnostics.Trace.WriteLine(message);
    }

    public CSharpSymbolExtractor()
    {
        _treeCache = new TreeCache(maxEntries: 500);
        _parseLock = new Threading.TimeoutLock("CSharpExtractor.Parse", TimeSpan.FromSeconds(30), Log);
    }

    internal CSharpSymbolExtractor(TreeCache treeCache)
    {
        _treeCache = treeCache;
        _parseLock = new Threading.TimeoutLock("CSharpExtractor.Parse", TimeSpan.FromSeconds(30), Log);
    }

    /// <summary>
    /// Creates an extractor bound to a dedicated parser, bypassing the global TreeSitterParserPool lock.
    /// Use only in parallel/async contexts where all files processed by this instance are accessed
    /// from a single thread, so no cross-thread contention on TreeSitter's non-thread-safe Parser.
    /// </summary>
    internal CSharpSymbolExtractor(TreeSitterParser dedicatedParser)
    {
        ArgumentNullException.ThrowIfNull(dedicatedParser);

        _treeCache = new TreeCache(maxEntries: 500);
        _parseLock = new Threading.TimeoutLock("CSharpExtractor.Parse", TimeSpan.FromSeconds(30), Log);
        _dedicatedParser = dedicatedParser;
    }

    public ExtractionResult ExtractAll(string sourceCode, string filePath)
    {
        ArgumentNullException.ThrowIfNull(sourceCode);
        ArgumentNullException.ThrowIfNull(filePath);

        using var parseScope = _parseLock.Acquire();
        return ExtractAllCore(sourceCode, filePath);
    }

    public async Task<ExtractionResult> ExtractAllAsync(string sourceCode, string filePath, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sourceCode);
        ArgumentNullException.ThrowIfNull(filePath);

        using var parseScope = await _parseLock.AcquireAsync(ct).ConfigureAwait(false);
        return ExtractAllCore(sourceCode, filePath);
    }

    private ExtractionResult ExtractAllCore(string sourceCode, string filePath)
    {
        var tree = ParseIncremental(sourceCode, filePath);

        try
        {
            var symbols = new List<SymbolInfo>();
            CollectSymbols(tree.RootNode, filePath, symbols, parentSymbol: null, parentFqn: null, ns: null);

            var calls = _callExtractor.ExtractCallsFromTree(tree.RootNode, filePath, symbols);
            var deps = _dependencyExtractor.ExtractDependenciesFromTree(tree.RootNode, filePath, symbols);

            return new ExtractionResult
            {
                Symbols = symbols,
                Calls = calls,
                Dependencies = deps
            };
        }
        finally
        {
            _treeCache.Add(filePath, tree, sourceCode);
        }
    }

    public IReadOnlyList<SymbolInfo> ExtractSymbols(string sourceCode, string filePath)
    {
        ArgumentNullException.ThrowIfNull(sourceCode);
        ArgumentNullException.ThrowIfNull(filePath);

        using var parseScope = _parseLock.Acquire();
        return ExtractSymbolsCore(sourceCode, filePath);
    }

    public async Task<IReadOnlyList<SymbolInfo>> ExtractSymbolsAsync(string sourceCode, string filePath, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sourceCode);
        ArgumentNullException.ThrowIfNull(filePath);

        using var parseScope = await _parseLock.AcquireAsync(ct).ConfigureAwait(false);
        return ExtractSymbolsCore(sourceCode, filePath);
    }

    private IReadOnlyList<SymbolInfo> ExtractSymbolsCore(string sourceCode, string filePath)
    {
        var tree = ParseIncremental(sourceCode, filePath);

        try
        {
            var symbols = new List<SymbolInfo>();
            CollectSymbols(tree.RootNode, filePath, symbols, parentSymbol: null, parentFqn: null, ns: null);
            return symbols;
        }
        finally
        {
            _treeCache.Add(filePath, tree, sourceCode);
        }
    }

    private Tree ParseIncremental(string sourceCode, string filePath)
    {
        // Dedicated parser → bypass global pool lock for true parallelism.
        // Shared parser → acquire global lock because TreeSitter Parser is NOT thread-safe.
        using var sharedLock = _dedicatedParser is null ? TreeSitterParserPool.AcquireShared() : null;
        var parser = _dedicatedParser ?? TreeSitterParserPool.Shared;

        if (_treeCache.TryGet(filePath, out var cachedTree) && cachedTree is not null)
        {
            var oldSource = _treeCache.GetSource(filePath);
            if (oldSource is not null)
            {
                var edit = SourceDiff.ComputeEdit(oldSource, sourceCode);
                cachedTree.Edit(edit);
                return parser.Parse(sourceCode, cachedTree);
            }
        }

        return parser.Parse(sourceCode);
    }

    public void Dispose()
    {
        if (!DisposableHelper.TryMarkDisposed(ref _disposed)) return;

        _treeCache.Dispose();
        _dedicatedParser?.Dispose();
    }

    public IReadOnlyList<CallEdge> ExtractCalls(string sourceCode, string filePath, IReadOnlyList<SymbolInfo> symbols)
    {
        return _callExtractor.ExtractCalls(sourceCode, filePath, symbols);
    }

    public IReadOnlyList<DependencyEdge> ExtractDependencies(string sourceCode, string filePath, IReadOnlyList<SymbolInfo> symbols)
    {
        return _dependencyExtractor.ExtractDependencies(sourceCode, filePath, symbols);
    }

    private static void CollectSymbols(Node node, string filePath, List<SymbolInfo> symbols,
        string? parentSymbol, string? parentFqn, string? ns)
    {
        if (node.IsError)
        {
            return;
        }

        if (node.Type == "compilation_unit")
        {
            CollectCompilationUnitChildren(node, filePath, symbols);
            return;
        }

        if (TryExtractSymbol(node, filePath, parentSymbol, parentFqn, ns, out var symbol, out var currentNs))
        {
            symbols.Add(symbol);

            if (TryExtractPrimaryConstructor(node, filePath, symbol.Name, symbol.FullyQualifiedName, currentNs, symbols))
            {
            }

            foreach (var child in node.NamedChildren)
            {
                CollectSymbols(child, filePath, symbols, symbol.Name, symbol.FullyQualifiedName, currentNs);
            }
            return;
        }

        if (TryExtractFieldSymbols(node, filePath, parentSymbol, parentFqn, ns, symbols))
        {
            return;
        }

        foreach (var child in node.NamedChildren)
        {
            CollectSymbols(child, filePath, symbols, parentSymbol, parentFqn, ns);
        }
    }

    private static void CollectCompilationUnitChildren(Node node, string filePath, List<SymbolInfo> symbols)
    {
        string? fileScopedNs = null;

        foreach (var child in node.NamedChildren)
        {
            if (child.Type == "file_scoped_namespace_declaration")
            {
                var nameNode = child.GetChildForField("name");
                var nsName = nameNode?.Text ?? "<anonymous>";

                symbols.Add(new SymbolInfo
                {
                    Name = nsName,
                    FullyQualifiedName = nsName,
                    Kind = SymbolKind.Namespace,
                    FilePath = filePath,
                    StartLine = child.StartPosition.Row + 1,
                    EndLine = child.EndPosition.Row + 1,
                    StartColumn = child.StartPosition.Column,
                    EndColumn = child.EndPosition.Column,
                    ParentSymbol = null,
                    Namespace = null,
                    Accessibility = null
                });

                fileScopedNs = nsName;

                foreach (var nsChild in child.NamedChildren)
                {
                    if (nsChild.Type != "identifier")
                    {
                        CollectSymbols(nsChild, filePath, symbols, null, null, fileScopedNs);
                    }
                }

                continue;
            }

            CollectSymbols(child, filePath, symbols, null, null, fileScopedNs);
        }
    }

    private static bool TryExtractSymbol(Node node, string filePath, string? parentSymbol, string? parentFqn, string? ns,
        [NotNullWhen(true)] out SymbolInfo? symbol, out string? currentNs)
    {
        symbol = null;
        currentNs = ns;

        if (!NodeTypeToKind.TryGetValue(node.Type, out var kind))
        {
            return false;
        }

        if (kind == SymbolKind.Record && HasModifier(node, "struct"))
        {
            kind = SymbolKind.RecordStruct;
        }

        var name = ExtractSymbolName(node, kind);

        currentNs = kind == SymbolKind.Namespace ? name : ns;

        var fqn = ComputeFqn(name, parentFqn, ns, kind);
        var accessibility = GetAccessibility(node);

        symbol = new SymbolInfo
        {
            Name = name,
            FullyQualifiedName = fqn,
            Kind = kind,
            FilePath = filePath,
            StartLine = node.StartPosition.Row + 1,
            EndLine = node.EndPosition.Row + 1,
            StartColumn = node.StartPosition.Column,
            EndColumn = node.EndPosition.Column,
            ParentSymbol = parentSymbol,
            Namespace = ns,
            Accessibility = accessibility
        };

        return true;
    }

    private static string ExtractSymbolName(Node node, SymbolKind kind)
    {
        switch (kind)
        {
            case SymbolKind.Operator:
                return ExtractOperatorName(node);
            case SymbolKind.Indexer:
                return "this";
            case SymbolKind.Destructor:
                return ExtractDestructorName(node);
            case SymbolKind.Constructor:
                return "ctor";
            default:
                var nameNode = node.GetChildForField("name");
                return nameNode?.Text ?? "<anonymous>";
        }
    }

    private static string ExtractOperatorName(Node node)
    {
        var nameNode = node.GetChildForField("name");
        if (nameNode is not null)
        {
            return $"op_{nameNode.Text}";
        }

        var foundOperator = false;
        foreach (var child in node.Children)
        {
            if (child.Type == "operator")
            {
                foundOperator = true;
                continue;
            }

            if (foundOperator && child.IsNamed)
            {
                return $"op_{child.Text}";
            }

            if (foundOperator && !child.IsNamed)
            {
                return $"op_{child.Text}";
            }
        }

        return "<operator>";
    }

    private static string ExtractDestructorName(Node node)
    {
        var nameNode = node.GetChildForField("name");
        if (nameNode is not null)
        {
            return $"Finalize";
        }

        return "Finalize";
    }

    private static bool TryExtractPrimaryConstructor(Node typeDeclNode, string filePath, string typeName, string typeFqn,
        string? ns, List<SymbolInfo> symbols)
    {
        if (typeDeclNode.Type is not ("class_declaration" or "struct_declaration" or "record_declaration" or "record_struct_declaration"))
        {
            return false;
        }

        Node? parameterList = null;
        foreach (var child in typeDeclNode.Children)
        {
            if (child.Type == "parameter_list")
            {
                parameterList = child;
                break;
            }
        }

        if (parameterList is null)
        {
            return false;
        }

        var hasParameters = false;
        foreach (var child in parameterList.NamedChildren)
        {
            if (child.Type == "parameter")
            {
                hasParameters = true;
                break;
            }
        }

        if (!hasParameters)
        {
            return false;
        }

        var name = "ctor";
        var fqn = $"{typeFqn}.{name}";
        var accessibility = GetAccessibility(typeDeclNode);

        symbols.Add(new SymbolInfo
        {
            Name = name,
            FullyQualifiedName = fqn,
            Kind = SymbolKind.Constructor,
            FilePath = filePath,
            StartLine = parameterList.StartPosition.Row + 1,
            EndLine = parameterList.EndPosition.Row + 1,
            StartColumn = parameterList.StartPosition.Column,
            EndColumn = parameterList.EndPosition.Column,
            ParentSymbol = typeName,
            Namespace = ns,
            Accessibility = accessibility
        });

        return true;
    }

    private static string ComputeFqn(string name, string? parentFqn, string? ns, SymbolKind kind)
    {
        if (kind == SymbolKind.Namespace)
        {
            return name;
        }

        if (!string.IsNullOrEmpty(parentFqn))
        {
            return $"{parentFqn}.{name}";
        }

        if (!string.IsNullOrEmpty(ns))
        {
            return $"{ns}.{name}";
        }

        return name;
    }

    private static bool TryExtractFieldSymbols(Node node, string filePath, string? parentSymbol, string? parentFqn, string? ns,
        List<SymbolInfo> symbols)
    {
        if (node.Type is not ("field_declaration" or "event_field_declaration"))
        {
            return false;
        }

        var isConst = HasModifier(node, "const");
        var kind = isConst ? SymbolKind.Constant : SymbolKind.Field;
        var accessibility = GetAccessibility(node);

        foreach (var child in node.NamedChildren)
        {
            if (child.Type == "variable_declaration")
            {
                ExtractVariableDeclarators(child, filePath, parentSymbol, parentFqn, ns, kind, accessibility, symbols);
            }
        }

        return true;
    }

    private static void ExtractVariableDeclarators(Node variableDecl, string filePath, string? parentSymbol,
        string? parentFqn, string? ns, SymbolKind kind, string? accessibility, List<SymbolInfo> symbols)
    {
        foreach (var child in variableDecl.NamedChildren)
        {
            if (child.Type == "variable_declarator")
            {
                var nameNode = child.GetChildForField("name");
                if (nameNode is null)
                {
                    continue;
                }

                var name = nameNode.Text;
                var fqn = ComputeFqn(name, parentFqn, ns, kind);

                symbols.Add(new SymbolInfo
                {
                    Name = name,
                    FullyQualifiedName = fqn,
                    Kind = kind,
                    FilePath = filePath,
                    StartLine = child.StartPosition.Row + 1,
                    EndLine = child.EndPosition.Row + 1,
                    StartColumn = child.StartPosition.Column,
                    EndColumn = child.EndPosition.Column,
                    ParentSymbol = parentSymbol,
                    Namespace = ns,
                    Accessibility = accessibility
                });
            }
        }
    }

    private static bool HasModifier(Node node, string modifierText)
    {
        return node.Children.Any(c =>
            (c.Type == "modifier" && c.Text == modifierText) || c.Type == modifierText);
    }

    private static string? GetAccessibility(Node node)
    {
        return node.Children
            .Where(c => (c.Type == "modifier" && AccessibilityModifiers.Contains(c.Text))
                     || AccessibilityModifiers.Contains(c.Type))
            .Select(c => c.Type == "modifier" ? c.Text : c.Type)
            .FirstOrDefault();
    }
}
