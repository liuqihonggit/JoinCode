namespace JoinCode.CodeIndex.Ast;

/// <summary>TreeSitter field ID 预计算 — 消除 GetChildForField(string) 的 UTF8 编码+byte[] 分配</summary>
internal static class TreeSitterFieldIds {
    private static ushort _name;
    private static ushort _type;
    private static ushort _function;
    private static ushort _expression;
    private static ushort _right;
    private static ushort _returns;
    private static bool _initialized;

    /// <summary>从 Language 预计算全部 field ID（仅首次调用生效）</summary>
    public static void Initialize(TreeSitter.Language language) {
        if (_initialized) return;
        _name = language.GetFieldIdFromName("name");
        _type = language.GetFieldIdFromName("type");
        _function = language.GetFieldIdFromName("function");
        _expression = language.GetFieldIdFromName("expression");
        _right = language.GetFieldIdFromName("right");
        _returns = language.GetFieldIdFromName("returns");
        _initialized = true;
    }

    /// <summary>"name" 字段 ID</summary>
    public static ushort Name => _name;
    /// <summary>"type" 字段 ID</summary>
    public static ushort Type => _type;
    /// <summary>"function" 字段 ID</summary>
    public static ushort Function => _function;
    /// <summary>"expression" 字段 ID</summary>
    public static ushort Expression => _expression;
    /// <summary>"right" 字段 ID</summary>
    public static ushort Right => _right;
    /// <summary>"returns" 字段 ID</summary>
    public static ushort Returns => _returns;
}

/// <summary>单遍历构建的符号结构集合 — 替代 6 次独立 BuildXxx 遍历</summary>
internal sealed record SymbolStructures(
    HashSet<string> ClassNameSet,
    HashSet<string> InterfaceNameSet,
    List<MethodLookupEntry> MethodLookup,
    Dictionary<string, string> ExtensionMethodMap,
    SymbolIndex SymbolIndex,
    Dictionary<string, string> TypeFqnMap);

/// <summary>
/// C# 符号提取器 — 基于 Tree-sitter 解析 C# 源码,提取符号、调用、依赖信息
/// 实现 ILanguagePlugin 接口,支持增量解析与并行提取
/// </summary>
public sealed class CSharpSymbolExtractor : ILanguagePlugin, IDisposable {
    private static readonly FrozenDictionary<string, SymbolKind> NodeTypeToKind = new Dictionary<string, SymbolKind>() {
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

    /// <summary>语言标识符 — 用于匹配文件扩展名对应的语言插件</summary>
    public string LanguageId => "c-sharp";

    /// <summary>支持的文件扩展名列表 — 仅 .cs 文件</summary>
    public IReadOnlyList<string> FileExtensions => [".cs"];

    private readonly CSharpCallExtractor _callExtractor = new();
    private readonly CSharpDependencyExtractor _dependencyExtractor = new();
    private readonly TreeCache _treeCache;
    private readonly Threading.TimeoutLock _parseLock;
    private readonly TreeSitterParser? _dedicatedParser;
    private readonly ILogger? _logger;
    private int _disposed;

    private void Log(string message) {
        _logger?.LogDebug(message);
    }

    /// <summary>
    /// 构造 C# 符号提取器 — 使用内部树缓存与解析锁
    /// </summary>
    /// <param name="logger">日志记录器(可选)</param>
    public CSharpSymbolExtractor(ILogger? logger = null) {
        _logger = logger;
        _treeCache = new TreeCache(maxEntries: 500, logger);
        _parseLock = new Threading.TimeoutLock("CSharpExtractor.Parse", TimeSpan.FromSeconds(30), Log);
    }

    /// <summary>
    /// 内部构造 — 注入自定义树缓存,用于测试场景
    /// </summary>
    /// <param name="treeCache">树缓存实例</param>
    /// <param name="logger">日志记录器(可选)</param>
    internal CSharpSymbolExtractor(TreeCache treeCache, ILogger? logger = null) {
        _logger = logger;
        _treeCache = treeCache;
        _parseLock = new Threading.TimeoutLock("CSharpExtractor.Parse", TimeSpan.FromSeconds(30), Log);
    }

    /// <summary>
    /// Creates an extractor bound to a dedicated parser, bypassing the global TreeSitterParserPool lock.
    /// Use only in parallel/async contexts where all files processed by this instance are accessed
    /// from a single thread, so no cross-thread contention on TreeSitter's non-thread-safe Parser.
    /// </summary>
    internal CSharpSymbolExtractor(TreeSitterParser dedicatedParser, ILogger? logger = null) {
        ArgumentNullException.ThrowIfNull(dedicatedParser);

        _logger = logger;
        _treeCache = new TreeCache(maxEntries: 500, logger);
        _parseLock = new Threading.TimeoutLock("CSharpExtractor.Parse", TimeSpan.FromSeconds(30), Log);
        _dedicatedParser = dedicatedParser;
    }

    /// <summary>
    /// 同步提取全部信息 — 符号、调用、依赖,在解析锁内执行
    /// </summary>
    /// <param name="sourceCode">源代码文本</param>
    /// <param name="filePath">文件路径,用于增量解析缓存键</param>
    /// <returns>提取结果,含符号、调用边、依赖边</returns>
    public ExtractionResult ExtractAll(string sourceCode, string filePath) {
        ArgumentNullException.ThrowIfNull(sourceCode);
        ArgumentNullException.ThrowIfNull(filePath);

        using var parseScope = _parseLock.Acquire();
        return ExtractAllCore(sourceCode, filePath);
    }

    /// <summary>
    /// 异步提取全部信息 — 符号、调用、依赖,在解析锁内执行
    /// </summary>
    /// <param name="sourceCode">源代码文本</param>
    /// <param name="filePath">文件路径,用于增量解析缓存键</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>提取结果,含符号、调用边、依赖边</returns>
    public async Task<ExtractionResult> ExtractAllAsync(string sourceCode, string filePath, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(sourceCode);
        ArgumentNullException.ThrowIfNull(filePath);

        using var parseScope = await _parseLock.AcquireAsync(ct).ConfigureAwait(false);
        return ExtractAllCore(sourceCode, filePath);
    }

    private ExtractionResult ExtractAllCore(string sourceCode, string filePath) {
        var tree = ParseIncremental(sourceCode, filePath);
        TreeSitterFieldIds.Initialize(tree.Language);

        try {
            var symbols = new List<SymbolInfo>();
            var rawVariableTypes = new Dictionary<string, string>(StringComparer.Ordinal);
            var interfaceNames = new HashSet<string>(StringComparer.Ordinal);
            CollectSymbols(tree.RootNode, filePath, symbols, parentSymbol: null, parentFqn: null, ns: null,
                rawVariableTypes, interfaceNames);

            // 单遍历构建全部符号结构（6→1 pass）
            var structures = BuildSymbolStructures(symbols);

            // 过滤 variableTypeMap：保留类型名首字母大写或在 classNameSet 中的条目
            var variableTypeMap = new Dictionary<string, string>(rawVariableTypes.Count, StringComparer.Ordinal);
            foreach (var (name, typeName) in rawVariableTypes) {
                if (typeName.Length > 0 && (char.IsUpper(typeName[0]) || structures.ClassNameSet.Contains(typeName))) {
                    variableTypeMap[name] = typeName;
                }
            }

            var calls = new List<CallEdge>();
            var fileFqn = CSharpDependencyExtractor.BuildFileFqn(filePath, symbols);
            var deps = new List<DependencyEdge>();
            var typeUsageMap = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

            // 联合主遍历：同时收集 calls、deps、类型引用
            CollectCallsAndDeps(tree.RootNode, filePath, structures.ClassNameSet, structures.InterfaceNameSet, variableTypeMap,
                structures.MethodLookup, structures.SymbolIndex, structures.ExtensionMethodMap, calls, fileFqn, deps, interfaceNames, structures.TypeFqnMap,
                null, typeUsageMap);

            // 从累积的类型引用创建 DependencyEdge
            foreach (var (sourceFqn, usedTypes) in typeUsageMap) {
                CSharpDependencyExtractor.BuildTypeUsageEdges(sourceFqn, filePath, usedTypes, deps, structures.TypeFqnMap);
            }

            var chunks = CollectChunksWithParents(sourceCode, filePath, symbols);

            return new ExtractionResult {
                Symbols = symbols,
                Calls = calls,
                Dependencies = deps,
                Chunks = chunks
            };
        } finally {
            _treeCache.Add(filePath, tree, sourceCode);
        }
    }

    /// <summary>单遍历构建全部符号结构 — 替代 6 次 BuildXxx 独立遍历</summary>
    internal static SymbolStructures BuildSymbolStructures(IReadOnlyList<SymbolInfo> symbols) {
        var classNameSet = new HashSet<string>(StringComparer.Ordinal);
        var interfaceNameSet = new HashSet<string>(StringComparer.Ordinal);
        var methodLookup = new List<MethodLookupEntry>(symbols.Count);
        var extensionMethodMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var typeFqnMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var byFqn = new Dictionary<string, SymbolInfo>(symbols.Count, StringComparer.Ordinal);
        var byName = new Dictionary<string, List<SymbolInfo>>(symbols.Count, StringComparer.Ordinal);

        foreach (var s in symbols) {
            if (!byFqn.ContainsKey(s.FullyQualifiedName)) {
                byFqn[s.FullyQualifiedName] = s;
            }
            if (!byName.TryGetValue(s.Name, out var nameList)) {
                nameList = new List<SymbolInfo>(1);
                byName[s.Name] = nameList;
            }
            nameList.Add(s);

            switch (s.Kind) {
                case SymbolKind.Class or SymbolKind.Struct:
                    classNameSet.Add(s.Name);
                    if (!typeFqnMap.ContainsKey(s.Name)) {
                        typeFqnMap[s.Name] = s.FullyQualifiedName;
                    }
                    break;
                case SymbolKind.Interface:
                    interfaceNameSet.Add(s.Name);
                    if (!typeFqnMap.ContainsKey(s.Name)) {
                        typeFqnMap[s.Name] = s.FullyQualifiedName;
                    }
                    break;
                case SymbolKind.Enum or SymbolKind.Delegate or SymbolKind.Record or SymbolKind.RecordStruct:
                    if (!typeFqnMap.ContainsKey(s.Name)) {
                        typeFqnMap[s.Name] = s.FullyQualifiedName;
                    }
                    break;
                case SymbolKind.Method:
                    methodLookup.Add(new MethodLookupEntry(s.FullyQualifiedName, s.StartLine, s.EndLine));
                    if (s.ParentSymbol is not null) {
                        extensionMethodMap[s.Name] = s.ParentSymbol;
                    }
                    break;
                case SymbolKind.Constructor or SymbolKind.Property or SymbolKind.LocalFunction:
                    methodLookup.Add(new MethodLookupEntry(s.FullyQualifiedName, s.StartLine, s.EndLine));
                    break;
            }
        }

        methodLookup.Sort(static (a, b) => a.StartLine.CompareTo(b.StartLine));
        return new SymbolStructures(classNameSet, interfaceNameSet, methodLookup, extensionMethodMap, new SymbolIndex(byFqn, byName), typeFqnMap);
    }

    /// <summary>联合主遍历 — 单次 AST 遍历同时收集调用边、依赖边、类型引用（合并 CollectCalls + CollectDependencies + CollectTypeReferences）</summary>
    private static void CollectCallsAndDeps(
        Node node, string filePath,
        HashSet<string> classNameSet, HashSet<string> interfaceNameSet,
        Dictionary<string, string> variableTypeMap, List<MethodLookupEntry> methodLookup,
        SymbolIndex symbols, Dictionary<string, string> extensionMethodMap,
        List<CallEdge> calls,
        string fileFqn, List<DependencyEdge> deps, HashSet<string> interfaceNames,
        Dictionary<string, string> typeFqnMap,
        string? currentTypeFqn, Dictionary<string, HashSet<string>> typeUsageMap) {
        if (node.IsError) return;

        var nodeType = node.Type;

        CSharpCallExtractor.ProcessCallNode(node, nodeType, filePath, classNameSet, interfaceNameSet, variableTypeMap,
            methodLookup, symbols, extensionMethodMap, calls);
        CSharpDependencyExtractor.ProcessDepNode(node, nodeType, fileFqn, filePath, deps, interfaceNames, typeFqnMap);

        if (currentTypeFqn is not null) {
            if (!typeUsageMap.TryGetValue(currentTypeFqn, out var usedTypes)) {
                usedTypes = new HashSet<string>(StringComparer.Ordinal);
                typeUsageMap[currentTypeFqn] = usedTypes;
            }
            CSharpDependencyExtractor.ProcessTypeReferenceNode(node, nodeType, usedTypes);
        }

        var newTypeFqn = currentTypeFqn;
        if (CSharpDependencyExtractor.TypeDeclNodeTypes.Contains(nodeType)) {
            newTypeFqn = CSharpDependencyExtractor.TryGetTypeDeclSourceFqn(node, typeFqnMap);
        }

        var children = node.NamedChildren;
        var childCount = children.Count;
        for (var ci = 0; ci < childCount; ci++) {
            var child = children[ci];
            CollectCallsAndDeps(child, filePath, classNameSet, interfaceNameSet, variableTypeMap,
                methodLookup, symbols, extensionMethodMap, calls, fileFqn, deps, interfaceNames, typeFqnMap,
                newTypeFqn, typeUsageMap);
        }
    }

    /// <summary>
    /// 从符号列表生成代码块 — 建立父子层级关系，父文档定位信息直接写入 ChunkInfo。
    /// <para>类级别符号（Class/Struct/Interface/Record/Enum/Delegate）自身即父文档，无需额外定位。</para>
    /// <para>子符号（Method/Property/Field）的 ParentFilePath/StartLine/EndLine 指向所属类；顶级子符号指向文件级。</para>
    /// </summary>
    private static IReadOnlyList<ChunkInfo>
        CollectChunksWithParents(string sourceCode, string filePath, IReadOnlyList<SymbolInfo> symbols) {
        if (symbols.Count == 0) return [];

        var lines = sourceCode.Split('\n');
        var chunks = new List<ChunkInfo>(symbols.Count);

        var classFqnToLocation = new Dictionary<string, (int StartLine, int EndLine)>();
        foreach (var symbol in symbols) {
            if (!IsParentDocumentKind(symbol.Kind)) continue;
            var sourceText = ExtractLineRange(lines, symbol.StartLine, symbol.EndLine);
            if (string.IsNullOrWhiteSpace(sourceText)) continue;
            classFqnToLocation[symbol.FullyQualifiedName] = (symbol.StartLine, symbol.EndLine);
        }

        var hasTopLevelChildren = false;
        foreach (var symbol in symbols) {
            if (IsParentDocumentKind(symbol.Kind)) continue;
            var parentFqn = GetParentFqn(symbol.FullyQualifiedName);
            if (parentFqn is null || !classFqnToLocation.ContainsKey(parentFqn)) {
                hasTopLevelChildren = true;
                break;
            }
        }

        var fileParentStartLine = 1;
        var fileParentEndLine = lines.Length;

        foreach (var symbol in symbols) {
            var sourceText = ExtractLineRange(lines, symbol.StartLine, symbol.EndLine);
            if (string.IsNullOrWhiteSpace(sourceText)) continue;

            var contentHash = HashUtility.ComputeContentHash(sourceText);
            var chunkId = HashUtility.ComputeContentHash($"{filePath}|{symbol.FullyQualifiedName}|{contentHash}");

            string? parentFilePath = null;
            var parentStartLine = 0;
            var parentEndLine = 0;
            string? parentSymbolFqn = null;

            if (!IsParentDocumentKind(symbol.Kind)) {
                var parentFqn = GetParentFqn(symbol.FullyQualifiedName);
                if (parentFqn is not null && classFqnToLocation.TryGetValue(parentFqn, out var loc)) {
                    parentFilePath = filePath;
                    parentStartLine = loc.StartLine;
                    parentEndLine = loc.EndLine;
                    parentSymbolFqn = parentFqn;
                } else if (hasTopLevelChildren) {
                    parentFilePath = filePath;
                    parentStartLine = fileParentStartLine;
                    parentEndLine = fileParentEndLine;
                    parentSymbolFqn = filePath;
                }
            }

            chunks.Add(new ChunkInfo {
                ChunkId = chunkId,
                SymbolFqn = symbol.FullyQualifiedName,
                Kind = symbol.Kind,
                FilePath = filePath,
                StartLine = symbol.StartLine,
                EndLine = symbol.EndLine,
                LanguageId = "c-sharp",
                ContentHash = contentHash,
                SourceText = sourceText,
                ParentFilePath = parentFilePath,
                ParentStartLine = parentStartLine,
                ParentEndLine = parentEndLine,
                ParentSymbolFqn = parentSymbolFqn
            });
        }

        return chunks;
    }

    /// <summary>
    /// 判断符号类型是否可作为父文档（类级别容器类型）。
    /// </summary>
    private static bool IsParentDocumentKind(SymbolKind kind) =>
        kind is SymbolKind.Class or SymbolKind.Struct or SymbolKind.Interface
            or SymbolKind.Record or SymbolKind.RecordStruct
            or SymbolKind.Enum or SymbolKind.Delegate;

    /// <summary>
    /// 从完全限定名中提取父级 FQN — 去掉最后一段（如 N.C.M → N.C）。
    /// </summary>
    private static string? GetParentFqn(string fqn) {
        var lastDot = fqn.LastIndexOf('.');
        return lastDot <= 0 ? null : fqn[..lastDot];
    }

    /// <summary>
    /// 从行数组中提取指定行范围（1-indexed, 闭区间）。
    /// </summary>
    private static string ExtractLineRange(string[] lines, int startLine, int endLine) {
        if (startLine < 1 || endLine < startLine || startLine > lines.Length) {
            return string.Empty;
        }
        var end = Math.Min(endLine, lines.Length);
        var span = lines.AsSpan((startLine - 1), (end - startLine + 1));
        return string.Join('\n', span);
    }

    /// <summary>
    /// 同步提取符号列表 — 仅符号,不含调用与依赖
    /// </summary>
    /// <param name="sourceCode">源代码文本</param>
    /// <param name="filePath">文件路径,用于增量解析缓存键</param>
    /// <returns>符号信息列表</returns>
    public IReadOnlyList<SymbolInfo> ExtractSymbols(string sourceCode, string filePath) {
        ArgumentNullException.ThrowIfNull(sourceCode);
        ArgumentNullException.ThrowIfNull(filePath);

        using var parseScope = _parseLock.Acquire();
        return ExtractSymbolsCore(sourceCode, filePath);
    }

    /// <summary>
    /// 异步提取符号列表 — 仅符号,不含调用与依赖
    /// </summary>
    /// <param name="sourceCode">源代码文本</param>
    /// <param name="filePath">文件路径,用于增量解析缓存键</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>符号信息列表</returns>
    public async Task<IReadOnlyList<SymbolInfo>> ExtractSymbolsAsync(string sourceCode, string filePath, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(sourceCode);
        ArgumentNullException.ThrowIfNull(filePath);

        using var parseScope = await _parseLock.AcquireAsync(ct).ConfigureAwait(false);
        return ExtractSymbolsCore(sourceCode, filePath);
    }

    private IReadOnlyList<SymbolInfo> ExtractSymbolsCore(string sourceCode, string filePath) {
        var tree = ParseIncremental(sourceCode, filePath);

        try {
            var symbols = new List<SymbolInfo>();
            CollectSymbols(tree.RootNode, filePath, symbols, parentSymbol: null, parentFqn: null, ns: null,
                new Dictionary<string, string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal));
            return symbols;
        } finally {
            _treeCache.Add(filePath, tree, sourceCode);
        }
    }

    private Tree ParseIncremental(string sourceCode, string filePath) {
        // Dedicated parser → bypass global pool lock for true parallelism.
        // Shared parser → acquire global lock because TreeSitter Parser is NOT thread-safe.
        using var sharedLock = _dedicatedParser is null ? TreeSitterParserPool.AcquireShared() : null;
        var parser = _dedicatedParser ?? TreeSitterParserPool.Shared;

        if (_treeCache.TryGet(filePath, out var cachedTree) && cachedTree is not null) {
            var oldSource = _treeCache.GetSource(filePath);
            if (oldSource is not null) {
                var edit = SourceDiff.ComputeEdit(oldSource, sourceCode);
                cachedTree.Edit(edit);
                return parser.Parse(sourceCode, cachedTree);
            }
        }

        return parser.Parse(sourceCode);
    }

    /// <summary>
    /// 释放内部资源 — 树缓存与专用解析器(如有)
    /// </summary>
    public void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _treeCache.Dispose();
        _dedicatedParser?.Dispose();
        _parseLock.Dispose();
    }

    /// <summary>
    /// 提取调用边 — 委托给 CSharpCallExtractor
    /// </summary>
    /// <param name="sourceCode">源代码文本</param>
    /// <param name="filePath">文件路径</param>
    /// <param name="symbols">已提取的符号列表,用于解析调用方与被调用方</param>
    /// <returns>调用边列表</returns>
    public IReadOnlyList<CallEdge> ExtractCalls(string sourceCode, string filePath, IReadOnlyList<SymbolInfo> symbols) {
        return _callExtractor.ExtractCalls(sourceCode, filePath, symbols);
    }

    /// <summary>
    /// 提取依赖边 — 委托给 CSharpDependencyExtractor
    /// </summary>
    /// <param name="sourceCode">源代码文本</param>
    /// <param name="filePath">文件路径</param>
    /// <param name="symbols">已提取的符号列表,用于解析依赖源与目标</param>
    /// <returns>依赖边列表</returns>
    public IReadOnlyList<DependencyEdge> ExtractDependencies(string sourceCode, string filePath, IReadOnlyList<SymbolInfo> symbols) {
        return _dependencyExtractor.ExtractDependencies(sourceCode, filePath, symbols);
    }

    private static void CollectSymbols(Node node, string filePath, List<SymbolInfo> symbols,
        string? parentSymbol, string? parentFqn, string? ns,
        Dictionary<string, string> rawVariableTypes, HashSet<string> interfaceNames) {
        if (node.IsError) {
            return;
        }

        var nodeType = node.Type;

        CSharpCallExtractor.ProcessVariableTypeNodeUnfiltered(node, nodeType, rawVariableTypes);
        CSharpDependencyExtractor.ProcessInterfaceNameNode(node, nodeType, interfaceNames);

        if (nodeType == "compilation_unit") {
            CollectCompilationUnitChildren(node, filePath, symbols, rawVariableTypes, interfaceNames);
            return;
        }

        if (TryExtractSymbol(node, nodeType, filePath, parentSymbol, parentFqn, ns, out var symbol, out var currentNs)) {
            symbols.Add(symbol);

            if (TryExtractPrimaryConstructor(node, filePath, symbol.Name, symbol.FullyQualifiedName, currentNs, symbols)) {
            }

            var children = node.NamedChildren;
            var childCount = children.Count;
            for (var ci = 0; ci < childCount; ci++) {
                CollectSymbols(children[ci], filePath, symbols, symbol.Name, symbol.FullyQualifiedName, currentNs, rawVariableTypes, interfaceNames);
            }
            return;
        }

        if (TryExtractFieldSymbols(node, filePath, parentSymbol, parentFqn, ns, symbols)) {
            return;
        }

        var fallbackChildren = node.NamedChildren;
        var fallbackCount = fallbackChildren.Count;
        for (var ci = 0; ci < fallbackCount; ci++) {
            CollectSymbols(fallbackChildren[ci], filePath, symbols, parentSymbol, parentFqn, ns, rawVariableTypes, interfaceNames);
        }
    }

    private static void CollectCompilationUnitChildren(Node node, string filePath, List<SymbolInfo> symbols,
        Dictionary<string, string> rawVariableTypes, HashSet<string> interfaceNames) {
        string? fileScopedNs = null;

        var children = node.NamedChildren;
        var childCount = children.Count;
        for (var ci = 0; ci < childCount; ci++) {
            var child = children[ci];
            var childType = child.Type;
            if (childType == "file_scoped_namespace_declaration") {
                var nameNode = child.GetChildForField(TreeSitterFieldIds.Name);
                var nsName = nameNode?.Text ?? "<anonymous>";

                symbols.Add(new SymbolInfo {
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

                var nsChildren = child.NamedChildren;
                var nsCount = nsChildren.Count;
                for (var ni = 0; ni < nsCount; ni++) {
                    var nsChild = nsChildren[ni];
                    if (nsChild.Type != "identifier") {
                        CollectSymbols(nsChild, filePath, symbols, null, null, fileScopedNs, rawVariableTypes, interfaceNames);
                    }
                }

                continue;
            }

            CollectSymbols(child, filePath, symbols, null, null, fileScopedNs, rawVariableTypes, interfaceNames);
        }
    }

    private static bool TryExtractSymbol(Node node, string nodeType, string filePath, string? parentSymbol, string? parentFqn, string? ns,
        [NotNullWhen(true)] out SymbolInfo? symbol, out string? currentNs) {
        symbol = null;
        currentNs = ns;

        if (!NodeTypeToKind.TryGetValue(nodeType, out var kind)) {
            return false;
        }

        if (kind == SymbolKind.Record && HasModifier(node, "struct")) {
            kind = SymbolKind.RecordStruct;
        }

        var name = ExtractSymbolName(node, kind);

        currentNs = kind == SymbolKind.Namespace ? name : ns;

        var fqn = ComputeFqn(name, parentFqn, ns, kind);
        var accessibility = GetAccessibility(node);

        symbol = new SymbolInfo {
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

    internal static string ExtractSymbolName(Node node, SymbolKind kind) {
        switch (kind) {
            case SymbolKind.Operator:
            return ExtractOperatorName(node);
            case SymbolKind.Indexer:
            return "this";
            case SymbolKind.Destructor:
            return ExtractDestructorName(node);
            case SymbolKind.Constructor:
            return "ctor";
            default:
            var nameNode = node.GetChildForField(TreeSitterFieldIds.Name);
            return nameNode?.Text ?? "<anonymous>";
        }
    }

    internal static string ExtractOperatorName(Node node) {
        var nameNode = node.GetChildForField(TreeSitterFieldIds.Name);
        if (nameNode is not null) {
            return $"op_{nameNode.Text}";
        }

        var foundOperator = false;
        foreach (var child in node.Children) {
            if (child.Type == "operator") {
                foundOperator = true;
                continue;
            }

            if (foundOperator && child.IsNamed) {
                return $"op_{child.Text}";
            }

            if (foundOperator && !child.IsNamed) {
                return $"op_{child.Text}";
            }
        }

        return "<operator>";
    }

    private static string ExtractDestructorName(Node node) {
        var nameNode = node.GetChildForField(TreeSitterFieldIds.Name);
        if (nameNode is not null) {
            return $"Finalize";
        }

        return "Finalize";
    }

    private static bool TryExtractPrimaryConstructor(Node typeDeclNode, string filePath, string typeName, string typeFqn,
        string? ns, List<SymbolInfo> symbols) {
        if (typeDeclNode.Type is not ("class_declaration" or "struct_declaration" or "record_declaration" or "record_struct_declaration")) {
            return false;
        }

        Node? parameterList = null;
        foreach (var child in typeDeclNode.Children) {
            if (child.Type == "parameter_list") {
                parameterList = child;
                break;
            }
        }

        if (parameterList is null) {
            return false;
        }

        var hasParameters = false;
        foreach (var child in parameterList.NamedChildren) {
            if (child.Type == "parameter") {
                hasParameters = true;
                break;
            }
        }

        if (!hasParameters) {
            return false;
        }

        var name = "ctor";
        var fqn = $"{typeFqn}.{name}";
        var accessibility = GetAccessibility(typeDeclNode);

        symbols.Add(new SymbolInfo {
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

    internal static string ComputeFqn(string name, string? parentFqn, string? ns, SymbolKind kind) {
        if (kind == SymbolKind.Namespace) {
            return name;
        }

        if (!string.IsNullOrEmpty(parentFqn)) {
            return $"{parentFqn}.{name}";
        }

        if (!string.IsNullOrEmpty(ns)) {
            return $"{ns}.{name}";
        }

        return name;
    }

    private static bool TryExtractFieldSymbols(Node node, string filePath, string? parentSymbol, string? parentFqn, string? ns,
        List<SymbolInfo> symbols) {
        if (node.Type is not ("field_declaration" or "event_field_declaration")) {
            return false;
        }

        var isConst = HasModifier(node, "const");
        var kind = isConst ? SymbolKind.Constant : SymbolKind.Field;
        var accessibility = GetAccessibility(node);

        foreach (var child in node.NamedChildren) {
            if (child.Type == "variable_declaration") {
                ExtractVariableDeclarators(child, filePath, parentSymbol, parentFqn, ns, kind, accessibility, symbols);
            }
        }

        return true;
    }

    private static void ExtractVariableDeclarators(Node variableDecl, string filePath, string? parentSymbol,
        string? parentFqn, string? ns, SymbolKind kind, string? accessibility, List<SymbolInfo> symbols) {
        foreach (var child in variableDecl.NamedChildren) {
            if (child.Type == "variable_declarator") {
                var nameNode = child.GetChildForField(TreeSitterFieldIds.Name);
                if (nameNode is null) {
                    continue;
                }

                var name = nameNode.Text;
                var fqn = ComputeFqn(name, parentFqn, ns, kind);

                symbols.Add(new SymbolInfo {
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

    private static bool HasModifier(Node node, string modifierText) {
        return node.Children.Any(c =>
            (c.Type == "modifier" && c.Text == modifierText) || c.Type == modifierText);
    }

    private static string? GetAccessibility(Node node) {
        return node.Children
            .Where(c => (c.Type == "modifier" && AccessibilityModifiers.Contains(c.Text))
                     || AccessibilityModifiers.Contains(c.Type))
            .Select(c => c.Type == "modifier" ? c.Text : c.Type)
            .FirstOrDefault();
    }
}