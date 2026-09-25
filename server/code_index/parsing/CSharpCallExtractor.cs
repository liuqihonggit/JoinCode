namespace JoinCode.CodeIndex.Ast;

/// <summary>
/// CollectCalls 方法的参数封装
/// </summary>
internal sealed record CollectCallsOptions(
    Node Node,
    string FilePath,
    HashSet<string> ClassNameSet,
    HashSet<string> InterfaceNameSet,
    Dictionary<string, string> VariableTypeMap,
    List<MethodLookupEntry> MethodLookup,
    SymbolIndex Symbols,
    Dictionary<string, string> ExtensionMethodMap,
    List<CallEdge> Calls);

/// <summary>
/// ResolveCalleeFqn 方法的参数封装
/// </summary>
internal sealed record ResolveCalleeOptions(
    Node InvocationNode,
    string CalleeName,
    string CallerFqn,
    CallKind CallKind,
    HashSet<string> ClassNameSet,
    HashSet<string> InterfaceNameSet,
    Dictionary<string, string> VariableTypeMap,
    SymbolIndex Symbols,
    Dictionary<string, string> ExtensionMethodMap);

/// <summary>
/// 方法查找条目
/// </summary>
internal sealed class MethodLookupEntry(string fqn, int startLine, int endLine) {
    /// <summary>方法全限定名</summary>
    public string Fqn { get; } = fqn;
    /// <summary>起始行号</summary>
    public int StartLine { get; } = startLine;
    /// <summary>结束行号</summary>
    public int EndLine { get; } = endLine;
}

/// <summary>
/// 符号索引 — 单一数据源，封装按全限定名和按名称查找，避免消费方线性扫描符号列表
/// </summary>
internal sealed class SymbolIndex {
    private readonly Dictionary<string, SymbolInfo> _byFqn;
    private readonly ILookup<string, SymbolInfo> _byName;

    /// <summary>从符号列表构建索引，保留同 FQN 首个（与 FirstOrDefault 语义一致）</summary>
    public SymbolIndex(IReadOnlyList<SymbolInfo> symbols) {
        _byFqn = new Dictionary<string, SymbolInfo>(symbols.Count, StringComparer.Ordinal);
        foreach (var s in symbols) {
            if (!_byFqn.ContainsKey(s.FullyQualifiedName)) {
                _byFqn[s.FullyQualifiedName] = s;
            }
        }
        _byName = symbols.ToLookup(s => s.Name, StringComparer.Ordinal);
    }

    /// <summary>按全限定名 O(1) 查找</summary>
    public bool TryGetByFqn(string fqn, [MaybeNullWhen(false)] out SymbolInfo symbol) => _byFqn.TryGetValue(fqn, out symbol);

    /// <summary>按名称查找，返回同名符号集合（通常 ≤3 个），消费方按 Kind 二次过滤</summary>
    public IEnumerable<SymbolInfo> GetByName(string name) => _byName[name];
}

/// <summary>
/// C# 调用关系提取器 — 基于 TreeSitter AST 解析调用表达式、对象创建、构造初始化器和事件处理器赋值
/// </summary>
public sealed class CSharpCallExtractor {
    /// <summary>
    /// 从源码提取调用关系
    /// </summary>
    /// <param name="sourceCode">C# 源代码</param>
    /// <param name="filePath">文件路径</param>
    /// <param name="symbols">已索引的符号列表</param>
    /// <returns>调用边列表</returns>
    public IReadOnlyList<CallEdge> ExtractCalls(string sourceCode, string filePath, IReadOnlyList<SymbolInfo> symbols) {
        ArgumentNullException.ThrowIfNull(sourceCode);
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(symbols);

        using var parser = TreeSitterParserPool.CreateDisposable();
        using var tree = parser.Parse(sourceCode);

        return ExtractCallsFromTree(tree.RootNode, filePath, symbols);
    }

    /// <summary>
    /// 从 AST 根节点提取调用关系
    /// </summary>
    /// <param name="rootNode">AST 根节点</param>
    /// <param name="filePath">文件路径</param>
    /// <param name="symbols">已索引的符号列表</param>
    /// <returns>调用边列表</returns>
    public IReadOnlyList<CallEdge> ExtractCallsFromTree(Node rootNode, string filePath, IReadOnlyList<SymbolInfo> symbols) {
        var classNameSet = BuildClassNameSet(symbols);
        var interfaceNameSet = BuildInterfaceNameSet(symbols);
        var variableTypeMap = BuildVariableTypeMap(rootNode, classNameSet);
        var methodLookup = BuildMethodLookup(symbols);
        var extensionMethodMap = BuildExtensionMethodMap(symbols);
        var symbolIndex = new SymbolIndex(symbols);
        var calls = new List<CallEdge>();

        var options = new CollectCallsOptions(rootNode, filePath, classNameSet, interfaceNameSet, variableTypeMap, methodLookup, symbolIndex, extensionMethodMap, calls);
        CollectCalls(options);

        return calls;
    }

    private static HashSet<string> BuildClassNameSet(IReadOnlyList<SymbolInfo> symbols) =>
        new(symbols
            .Where(s => s.Kind is SymbolKind.Class or SymbolKind.Struct)
            .Select(s => s.Name), StringComparer.Ordinal);

    private static HashSet<string> BuildInterfaceNameSet(IReadOnlyList<SymbolInfo> symbols) =>
        new(symbols
            .Where(s => s.Kind == SymbolKind.Interface)
            .Select(s => s.Name), StringComparer.Ordinal);

    private static List<MethodLookupEntry> BuildMethodLookup(IReadOnlyList<SymbolInfo> symbols) =>
        symbols
            .Where(s => s.Kind is SymbolKind.Method or SymbolKind.Constructor or SymbolKind.Property or SymbolKind.LocalFunction)
            .Select(s => new MethodLookupEntry(s.FullyQualifiedName, s.StartLine, s.EndLine))
            .ToList();

    private static Dictionary<string, string> BuildVariableTypeMap(Node rootNode, HashSet<string> classNameSet) {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        CollectVariableTypes(rootNode, classNameSet, map);
        return map;
    }

    private static void CollectVariableTypes(Node node, HashSet<string> classNameSet, Dictionary<string, string> map) {
        if (node.Type == "parameter") {
            CollectParameterVariableType(node, classNameSet, map);
        } else if (node.Type == "variable_declaration") {
            CollectVariableDeclarationTypes(node, map);
        }

        foreach (var child in node.NamedChildren) {
            CollectVariableTypes(child, classNameSet, map);
        }
    }

    /// <summary>
    /// 收集 parameter 变量类型（提取以扁平化嵌套）
    /// </summary>
    private static void CollectParameterVariableType(Node node, HashSet<string> classNameSet, Dictionary<string, string> map) {
        var nameNode = node.GetChildForField("name");
        var typeNode = node.GetChildForField("type");
        if (nameNode is null || typeNode is null) return;
        var typeName = typeNode.Text;
        if ((typeName.Length > 0 && char.IsUpper(typeName[0])) || classNameSet.Contains(typeName)) {
            map[nameNode.Text] = typeName;
        }
    }

    /// <summary>
    /// 收集 variable_declaration 变量类型（提取以扁平化嵌套）
    /// </summary>
    private static void CollectVariableDeclarationTypes(Node node, Dictionary<string, string> map) {
        var typeNode = node.GetChildForField("type");
        if (typeNode is null) return;
        var typeName = typeNode.Text;
        foreach (var child in node.NamedChildren) {
            if (child.Type != "variable_declarator") continue;
            var nameNode = child.GetChildForField("name");
            if (nameNode is not null) {
                map[nameNode.Text] = typeName;
            }
        }
    }

    private static Dictionary<string, string> BuildExtensionMethodMap(IReadOnlyList<SymbolInfo> symbols) {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var symbol in symbols) {
            if (symbol.Kind is SymbolKind.Method && symbol.ParentSymbol is not null) {
                map[symbol.Name] = symbol.ParentSymbol;
            }
        }

        return map;
    }

    private static void CollectCalls(CollectCallsOptions options) {
        var node = options.Node;
        var filePath = options.FilePath;
        var classNameSet = options.ClassNameSet;
        var interfaceNameSet = options.InterfaceNameSet;
        var variableTypeMap = options.VariableTypeMap;
        var methodLookup = options.MethodLookup;
        var symbols = options.Symbols;
        var extensionMethodMap = options.ExtensionMethodMap;
        var calls = options.Calls;

        if (node.IsError) {
            return;
        }

        if (node.Type == "invocation_expression") {
            if (IsNameofExpression(node)) {
                return;
            }

            var edge = TryExtractInvocationEdge(node, filePath, classNameSet, interfaceNameSet, variableTypeMap, methodLookup, symbols, extensionMethodMap);
            if (edge is not null) {
                calls.Add(edge);
            }
        } else if (node.Type == "object_creation_expression") {
            var edge = TryExtractConstructorEdge(node, filePath, methodLookup);
            if (edge is not null) {
                calls.Add(edge);
            }
        } else if (node.Type == "constructor_initializer") {
            var edge = TryExtractConstructorInitializerEdge(node, filePath, methodLookup, symbols);
            if (edge is not null) {
                calls.Add(edge);
            }
        } else if (node.Type == "assignment_expression") {
            var edge = TryExtractEventHandlerEdge(node, filePath, methodLookup, symbols);
            if (edge is not null) {
                calls.Add(edge);
            }
        }

        foreach (var child in node.NamedChildren) {
            CollectCalls(options with { Node = child });
        }
    }

    private static CallEdge? TryExtractEventHandlerEdge(Node assignmentNode, string filePath, List<MethodLookupEntry> methodLookup, SymbolIndex symbols) {
        var isEventAssignment = false;
        foreach (var child in assignmentNode.Children) {
            if (child.Type is "+=" or "-=") {
                isEventAssignment = true;
                break;
            }
        }

        if (!isEventAssignment) {
            return null;
        }

        var rightNode = assignmentNode.GetChildForField("right");
        if (rightNode is null) {
            return null;
        }

        var handlerName = rightNode.Type == "identifier"
            ? rightNode.Text
            : rightNode.Type == "member_access_expression"
                ? rightNode.GetChildForField("name")?.Text
                : null;

        if (handlerName is null) {
            return null;
        }

        var callerFqn = FindCallerFqn(assignmentNode, methodLookup);

        var parentClassFqn = ExtractParentClassFqn(callerFqn);
        if (parentClassFqn is not null) {
            var candidateFqn = $"{parentClassFqn}.{handlerName}";
            if (symbols.TryGetByFqn(candidateFqn, out var match)) {
                return new CallEdge {
                    CallerSymbol = callerFqn,
                    CalleeSymbol = candidateFqn,
                    CallSiteFilePath = filePath,
                    CallSiteLine = assignmentNode.StartPosition.Row + 1,
                    CallKind = CallKind.EventHandler
                };
            }
        }

        var globalMatch = symbols.GetByName(handlerName).FirstOrDefault(s => s.Kind is SymbolKind.Method);
        if (globalMatch is not null) {
            return new CallEdge {
                CallerSymbol = callerFqn,
                CalleeSymbol = globalMatch.FullyQualifiedName,
                CallSiteFilePath = filePath,
                CallSiteLine = assignmentNode.StartPosition.Row + 1,
                CallKind = CallKind.EventHandler
            };
        }

        return new CallEdge {
            CallerSymbol = callerFqn,
            CalleeSymbol = handlerName,
            CallSiteFilePath = filePath,
            CallSiteLine = assignmentNode.StartPosition.Row + 1,
            CallKind = CallKind.EventHandler
        };
    }

    private static bool IsNameofExpression(Node invocationNode) {
        var funcNode = invocationNode.GetChildForField("function");
        return funcNode?.Type == "identifier" && funcNode.Text == "nameof";
    }

    private static CallEdge? TryExtractConstructorInitializerEdge(Node initializerNode, string filePath, List<MethodLookupEntry> methodLookup, SymbolIndex symbols) {
        var callerFqn = FindCallerFqn(initializerNode, methodLookup);
        var parentClassFqn = ExtractParentClassFqn(callerFqn);

        var isBaseInitializer = false;
        foreach (var child in initializerNode.Children) {
            if (child.Type == "base") {
                isBaseInitializer = true;
                break;
            }
        }

        if (isBaseInitializer) {
            var baseClassName = FindBaseClassNameFromAst(initializerNode);
            if (baseClassName is not null) {
                var baseClassFqn = FindSymbolFqn(baseClassName, symbols);
                return new CallEdge {
                    CallerSymbol = callerFqn,
                    CalleeSymbol = $"{baseClassFqn}.ctor",
                    CallSiteFilePath = filePath,
                    CallSiteLine = initializerNode.StartPosition.Row + 1,
                    CallKind = CallKind.Constructor
                };
            }
        } else {
            if (parentClassFqn is not null) {
                return new CallEdge {
                    CallerSymbol = callerFqn,
                    CalleeSymbol = $"{parentClassFqn}.ctor",
                    CallSiteFilePath = filePath,
                    CallSiteLine = initializerNode.StartPosition.Row + 1,
                    CallKind = CallKind.Constructor
                };
            }
        }

        return null;
    }

    private static CallEdge? TryExtractInvocationEdge(Node invocationNode, string filePath, HashSet<string> classNameSet, HashSet<string> interfaceNameSet, Dictionary<string, string> variableTypeMap, List<MethodLookupEntry> methodLookup, SymbolIndex symbols, Dictionary<string, string> extensionMethodMap) {
        var calleeName = ExtractCalleeName(invocationNode);
        if (calleeName is null) {
            return null;
        }

        var callerFqn = FindCallerFqn(invocationNode, methodLookup);
        var callKind = DetermineCallKind(invocationNode, calleeName, classNameSet, interfaceNameSet, variableTypeMap);
        var calleeFqn = ResolveCalleeFqn(new ResolveCalleeOptions(invocationNode, calleeName, callerFqn, callKind, classNameSet, interfaceNameSet, variableTypeMap, symbols, extensionMethodMap));

        return new CallEdge {
            CallerSymbol = callerFqn,
            CalleeSymbol = calleeFqn,
            CallSiteFilePath = filePath,
            CallSiteLine = invocationNode.StartPosition.Row + 1,
            CallKind = callKind
        };
    }

    private static CallEdge? TryExtractConstructorEdge(Node objectCreationNode, string filePath, List<MethodLookupEntry> methodLookup) {
        var typeNode = objectCreationNode.GetChildForField("type");
        if (typeNode is null) {
            return null;
        }

        var typeName = typeNode.Text;
        var callerFqn = FindCallerFqn(objectCreationNode, methodLookup);

        return new CallEdge {
            CallerSymbol = callerFqn,
            CalleeSymbol = typeName,
            CallSiteFilePath = filePath,
            CallSiteLine = objectCreationNode.StartPosition.Row + 1,
            CallKind = CallKind.Constructor
        };
    }

    private static string? ExtractCalleeName(Node invocationNode) {
        var funcNode = invocationNode.GetChildForField("function");
        if (funcNode is null) {
            return null;
        }

        if (funcNode.Type == "identifier") {
            return funcNode.Text;
        }

        if (funcNode.Type == "member_access_expression") {
            var nameNode = funcNode.GetChildForField("name");
            return nameNode?.Text;
        }

        return funcNode.Text;
    }

    private static string ResolveCalleeFqn(ResolveCalleeOptions options) {
        var invocationNode = options.InvocationNode;
        var calleeName = options.CalleeName;
        var callerFqn = options.CallerFqn;
        var classNameSet = options.ClassNameSet;
        var interfaceNameSet = options.InterfaceNameSet;
        var variableTypeMap = options.VariableTypeMap;
        var symbols = options.Symbols;
        var extensionMethodMap = options.ExtensionMethodMap;

        var funcNode = invocationNode.GetChildForField("function");

        if (funcNode?.Type == "member_access_expression") {
            var resolved = ResolveMemberAccessCallee(funcNode, calleeName, callerFqn, classNameSet, interfaceNameSet, variableTypeMap, symbols, invocationNode);
            if (resolved is not null) return resolved;
        }

        if (funcNode?.Type == "identifier") {
            var resolved = ResolveIdentifierCallee(calleeName, callerFqn, symbols);
            if (resolved is not null) return resolved;
        }

        if (extensionMethodMap.TryGetValue(calleeName, out var parentClass)) {
            var classFqn = FindSymbolFqn(parentClass, symbols);
            return $"{classFqn}.{calleeName}";
        }

        return calleeName;
    }

    /// <summary>
    /// 解析 member_access_expression 的被调用方 FQN（提取以扁平化嵌套）
    /// </summary>
    private static string? ResolveMemberAccessCallee(
        Node funcNode, string calleeName, string callerFqn,
        HashSet<string> classNameSet, HashSet<string> interfaceNameSet,
        Dictionary<string, string> variableTypeMap, SymbolIndex symbols, Node invocationNode) {
        var expressionNode = funcNode.GetChildForField("expression");
        if (expressionNode is null) return null;
        var expressionText = expressionNode.Text;

        if (expressionText is "base" or "this") {
            return ResolveBaseOrThisCallee(expressionText, calleeName, callerFqn, symbols, invocationNode);
        }

        if (expressionNode.Type != "identifier") return null;

        if (variableTypeMap.TryGetValue(expressionText, out var typeName)) {
            var typeFqn = FindSymbolFqn(typeName, symbols);
            return $"{typeFqn}.{calleeName}";
        }

        if (classNameSet.Contains(expressionText)) {
            var classFqn = FindSymbolFqn(expressionText, symbols);
            return $"{classFqn}.{calleeName}";
        }

        if (interfaceNameSet.Contains(expressionText)) {
            var interfaceFqn = FindSymbolFqn(expressionText, symbols);
            return $"{interfaceFqn}.{calleeName}";
        }

        return null;
    }

    /// <summary>
    /// 解析 base/this 的被调用方 FQN（提取以扁平化嵌套）
    /// </summary>
    private static string? ResolveBaseOrThisCallee(string expressionText, string calleeName, string callerFqn, SymbolIndex symbols, Node invocationNode) {
        var parentClassFqn = ExtractParentClassFqn(callerFqn);
        if (parentClassFqn is null) return null;

        if (expressionText == "base") {
            var baseClassName = FindBaseClassNameFromAst(invocationNode);
            if (baseClassName is not null) {
                var baseClassFqn = FindSymbolFqn(baseClassName, symbols);
                return $"{baseClassFqn}.{calleeName}";
            }
        }

        return $"{parentClassFqn}.{calleeName}";
    }

    /// <summary>
    /// 解析 identifier 的被调用方 FQN（提取以扁平化嵌套）
    /// </summary>
    private static string? ResolveIdentifierCallee(string calleeName, string callerFqn, SymbolIndex symbols) {
        var parentClassFqn = ExtractParentClassFqn(callerFqn);
        if (parentClassFqn is not null) {
            var candidateFqn = $"{parentClassFqn}.{calleeName}";
            if (symbols.TryGetByFqn(candidateFqn, out var match)) {
                return candidateFqn;
            }
        }

        var callerAsParentFqn = $"{callerFqn}.{calleeName}";
        if (symbols.TryGetByFqn(callerAsParentFqn, out var callerMatch)) {
            return callerAsParentFqn;
        }

        var globalMatch = symbols.GetByName(calleeName).FirstOrDefault(s => s.Kind is SymbolKind.Method or SymbolKind.LocalFunction);
        if (globalMatch is not null) {
            return globalMatch.FullyQualifiedName;
        }

        return null;
    }

    private static string? ExtractParentClassFqn(string methodFqn) {
        var lastDot = methodFqn.LastIndexOf('.');
        if (lastDot <= 0) {
            return null;
        }

        return methodFqn[..lastDot];
    }

    private static string FindSymbolFqn(string name, SymbolIndex symbols) {
        var symbol = symbols.GetByName(name).FirstOrDefault(s => s.Kind is SymbolKind.Class or SymbolKind.Struct or SymbolKind.Interface);
        return symbol?.FullyQualifiedName ?? name;
    }

    private static string? FindBaseClassNameFromAst(Node node) {
        var current = node.Parent;
        while (current is not null) {
            if (current.Type == "class_declaration") {
                var baseList = FindChildByType(current, "base_list");
                if (baseList is not null) {
                    foreach (var child in baseList.NamedChildren) {
                        return child.Text;
                    }
                }

                return null;
            }

            current = current.Parent;
        }

        return null;
    }

    private static Node? FindChildByType(Node node, string type) {
        foreach (var child in node.NamedChildren) {
            if (child.Type == type) {
                return child;
            }
        }

        return null;
    }

    private static string FindCallerFqn(Node node, List<MethodLookupEntry> methodLookup) {
        var line = node.StartPosition.Row + 1;
        MethodLookupEntry? best = null;

        foreach (var entry in methodLookup) {
            if (line >= entry.StartLine && line <= entry.EndLine) {
                if (best is null || (entry.EndLine - entry.StartLine) < (best.EndLine - best.StartLine)) {
                    best = entry;
                }
            }
        }

        return best?.Fqn ?? "<global>";
    }

    private static CallKind DetermineCallKind(Node invocationNode, string calleeName, HashSet<string> classNameSet, HashSet<string> interfaceNameSet, Dictionary<string, string> variableTypeMap) {
        var funcNode = invocationNode.GetChildForField("function");

        if (funcNode?.Type == "identifier") {
            return CallKind.Direct;
        }

        if (funcNode?.Type == "member_access_expression") {
            return DetermineMemberAccessCallKind(funcNode, classNameSet, interfaceNameSet, variableTypeMap);
        }

        return CallKind.Direct;
    }

    /// <summary>
    /// 判断 member_access_expression 的调用类型（提取以扁平化嵌套）
    /// </summary>
    private static CallKind DetermineMemberAccessCallKind(Node funcNode, HashSet<string> classNameSet, HashSet<string> interfaceNameSet, Dictionary<string, string> variableTypeMap) {
        var expressionNode = funcNode.GetChildForField("expression");
        if (expressionNode is null) return CallKind.Direct;
        var expressionText = expressionNode.Text;

        if (expressionText is "base" or "this") {
            return CallKind.Virtual;
        }

        if (expressionNode.Type == "identifier") {
            if (interfaceNameSet.Contains(expressionText)) return CallKind.Virtual;
            if (variableTypeMap.ContainsKey(expressionText)) return CallKind.Virtual;
            if ((expressionText.Length > 0 && char.IsUpper(expressionText[0])) || classNameSet.Contains(expressionText)) return CallKind.Static;
        }

        if (expressionNode.Type is "member_access_expression" or "invocation_expression" or "object_creation_expression") {
            return CallKind.Static;
        }

        return CallKind.Direct;
    }
}