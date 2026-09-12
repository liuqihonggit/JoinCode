namespace JoinCode.CodeIndex.Ast;

public sealed class CSharpDependencyExtractor
{
    private static readonly FrozenSet<string> BclTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "var", "void", "object", "string", "int", "bool", "long", "double",
        "float", "byte", "char", "decimal", "short", "uint", "ulong", "ushort", "sbyte",
        "nint", "nuint",
        "Action", "Func", "Predicate", "Tuple", "ValueTuple",
        "List", "Dictionary", "HashSet", "Queue", "Stack", "LinkedList", "SortedDictionary", "SortedList", "SortedSet",
        "IEnumerable", "IEnumerator", "ICollection", "IList", "IDictionary", "IReadOnlyList", "IReadOnlyCollection", "IReadOnlyDictionary", "ISet",
        "Task", "TaskCompletionSource", "CancellationToken", "ValueTask",
        "Exception", "ArgumentException", "ArgumentNullException", "InvalidOperationException", "NotSupportedException", "NotImplementedException", "NullReferenceException", "IndexOutOfRangeException", "OverflowException", "TimeoutException", "OperationCanceledException", "AggregateException",
        "Attribute", "ObsoleteAttribute", "SerializableAttribute", "NonSerializedAttribute", "AttributeUsageAttribute", "ConditionalAttribute",
        "EventArgs", "EventHandler", "PropertyChangedEventHandler",
        "Stream", "FileStream", "MemoryStream", "BufferedStream", "StreamReader", "StreamWriter", "BinaryReader", "BinaryWriter", "TextReader", "TextWriter",
        "FileInfo", "DirectoryInfo", "Path", "File", "Directory",
        "StringBuilder", "StringReader", "StringWriter", "StringComparison", "StringSplitOptions",
        "Convert", "Math", "DateTime", "DateTimeOffset", "TimeSpan", "DateOnly", "TimeOnly",
        "Guid", "Uri", "Version", "Environment", "Console",
        "Array", "ReadOnlySpan", "Span", "ReadOnlyMemory", "Memory",
        "Nullable", "Lazy", "WeakReference",
        "Type", "Assembly", "MethodInfo", "PropertyInfo", "FieldInfo", "ConstructorInfo", "ParameterInfo",
        "CancellationTokenSource", "IProgress", "Progress",
        "ConcurrentDictionary", "ConcurrentQueue", "ConcurrentStack", "ConcurrentBag", "BlockingCollection",
        "ImmutableArray", "ImmutableList", "ImmutableDictionary", "ImmutableHashSet",
        "JsonSerializer", "JsonDocument", "JsonElement", "JsonObject", "JsonArray", "JsonValue",
        "HttpClient", "HttpRequestMessage", "HttpResponseMessage",
        "Regex", "Match", "Group", "Capture",
        "BitConverter", "Buffer", "Marshal",
        "INotifyPropertyChanged", "INotifyCollectionChanged", "IComparable", "IEquatable", "IFormattable", "ICloneable", "IDisposable", "IAsyncDisposable", "IConvertible",
        "KeyNotFoundException", "FileNotFoundException", "DirectoryNotFoundException", "DriveNotFoundException", "EndOfStreamException", "PathTooLongException",
        "InvalidOperationException", "ObjectDisposedException",
        "CultureInfo", "NumberFormatInfo", "DateTimeFormatInfo",
        "Stopwatch", "Timer", "TimerCallback",
        "Interlocked", "Volatile", "Monitor", "SemaphoreSlim", "ManualResetEventSlim", "AutoResetEvent", "ManualResetEvent",
        "TaskFactory", "TaskScheduler",
        "IAsyncEnumerable", "IAsyncEnumerator",
        "ObservableCollection", "ReadOnlyObservableCollection", "ReadOnlyCollection", "ReadOnlyDictionary",
        "BindingList",
        "SortedSet",
        "ConcurrentBag", "Partitioner", "OrderablePartitioner",
        "Channel", "ChannelReader", "ChannelWriter",
        "ArrayPool", "MemoryPool",
        "SequenceReader", "SequencePosition", "ReadOnlySequence",
        "DbContext", "DbSet", "DbConnection", "DbCommand", "DbDataReader",
        "IServiceProvider", "IServiceCollection", "IServiceScope",
        "ILogger", "ILoggerFactory", "ILoggerProvider", "LogLevel",
        "IConfiguration", "IConfigurationSection", "IOptions", "IOptionsMonitor", "IOptionsSnapshot",
        "IHostBuilder", "IHost", "IHostedService", "IApplicationBuilder", "IWebHostEnvironment"
    }.ToFrozenSet();

    private static readonly FrozenSet<string> TypeDeclNodeTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "class_declaration", "struct_declaration", "interface_declaration",
        "enum_declaration", "record_declaration", "record_struct_declaration"
    }.ToFrozenSet();

    private static readonly FrozenSet<string> SkipParentTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "class_declaration", "struct_declaration", "interface_declaration",
        "enum_declaration", "record_declaration", "namespace_declaration",
        "using_directive", "base_list"
    }.ToFrozenSet();

    public IReadOnlyList<DependencyEdge> ExtractDependencies(string sourceCode, string filePath, IReadOnlyList<SymbolInfo> symbols)
    {
        ArgumentNullException.ThrowIfNull(sourceCode);
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(symbols);

        using var parser = TreeSitterParserPool.CreateDisposable();
        using var tree = parser.Parse(sourceCode);

        return ExtractDependenciesFromTree(tree.RootNode, filePath, symbols);
    }

    public IReadOnlyList<DependencyEdge> ExtractDependenciesFromTree(Node rootNode, string filePath, IReadOnlyList<SymbolInfo> symbols)
    {
        var deps = new List<DependencyEdge>();
        var interfaceNames = CollectInterfaceNames(rootNode);
        var typeFqnMap = BuildTypeFqnMap(symbols);
        var fileFqn = BuildFileFqn(filePath, symbols);

        CollectDependencies(rootNode, fileFqn, filePath, deps, interfaceNames, typeFqnMap);

        return deps;
    }

    private static Dictionary<string, string> BuildTypeFqnMap(IReadOnlyList<SymbolInfo> symbols)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var symbol in symbols)
        {
            if (symbol.Kind is SymbolKind.Class or SymbolKind.Struct or SymbolKind.Interface or SymbolKind.Enum or SymbolKind.Delegate or SymbolKind.Record or SymbolKind.RecordStruct)
            {
                if (!map.ContainsKey(symbol.Name))
                {
                    map[symbol.Name] = symbol.FullyQualifiedName;
                }
            }
        }
        return map;
    }

    private static string BuildFileFqn(string filePath, IReadOnlyList<SymbolInfo> symbols)
    {
        var ns = symbols.FirstOrDefault(s => s.Kind == SymbolKind.Namespace);
        return ns is not null ? $"<{ns.Name}>.{Path.GetFileName(filePath)}" : $"<global>.{Path.GetFileName(filePath)}";
    }

    private static HashSet<string> CollectInterfaceNames(Node node)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        CollectInterfaceNamesRecursive(node, names);
        return names;
    }

    private static void CollectInterfaceNamesRecursive(Node node, HashSet<string> names)
    {
        if (node.Type == "interface_declaration")
        {
            var nameNode = node.GetChildForField("name");
            if (nameNode is not null)
            {
                names.Add(nameNode.Text);
            }
        }

        foreach (var child in node.NamedChildren)
        {
            CollectInterfaceNamesRecursive(child, names);
        }
    }

    private static void CollectDependencies(Node node, string fileFqn, string filePath, List<DependencyEdge> deps, HashSet<string> interfaceNames, Dictionary<string, string> typeFqnMap)
    {
        if (node.IsError)
        {
            return;
        }

        switch (node.Type)
        {
            case "class_declaration":
            case "struct_declaration":
            case "record_declaration":
            case "record_struct_declaration":
            case "interface_declaration":
                ExtractTypeDeclarationDependencies(node, filePath, deps, interfaceNames, typeFqnMap);
                break;
            case "using_directive":
                ExtractUsingDependency(node, fileFqn, filePath, deps);
                break;
        }

        foreach (var child in node.NamedChildren)
        {
            CollectDependencies(child, fileFqn, filePath, deps, interfaceNames, typeFqnMap);
        }
    }

    private static void ExtractTypeDeclarationDependencies(Node typeDeclNode, string filePath, List<DependencyEdge> deps, HashSet<string> interfaceNames, Dictionary<string, string> typeFqnMap)
    {
        var nameNode = typeDeclNode.GetChildForField("name");
        if (nameNode is null)
        {
            return;
        }

        var sourceName = nameNode.Text;
        var sourceFqn = typeFqnMap.TryGetValue(sourceName, out var fqn) ? fqn : sourceName;

        var baseList = FindChildByType(typeDeclNode, "base_list");
        if (baseList is not null)
        {
            foreach (var child in baseList.NamedChildren)
            {
                ExtractBaseListEntry(child, sourceFqn, filePath, deps, interfaceNames, typeFqnMap);
            }
        }

        ExtractTypeUsageDependencies(typeDeclNode, sourceFqn, filePath, deps, typeFqnMap);
        ExtractGenericConstraintDependencies(typeDeclNode, sourceFqn, filePath, deps, typeFqnMap);
        ExtractAttributeDependencies(typeDeclNode, sourceFqn, filePath, deps, typeFqnMap);
        ExtractContainsDependencies(typeDeclNode, sourceFqn, filePath, deps, typeFqnMap);
    }

    private static void ExtractBaseListEntry(Node baseEntry, string sourceFqn, string filePath, List<DependencyEdge> deps, HashSet<string> interfaceNames, Dictionary<string, string> typeFqnMap)
    {
        if (baseEntry.Type == "generic_name")
        {
            var genericName = GetGenericNameIdentifier(baseEntry);
            if (genericName is not null)
            {
                var targetFqn = typeFqnMap.TryGetValue(genericName, out var fqnVal) ? fqnVal : genericName;
                var kind = interfaceNames.Contains(genericName) ? DependencyKind.Implements : DependencyKind.Inherits;
                deps.Add(new DependencyEdge
                {
                    SourceSymbol = sourceFqn,
                    TargetSymbol = targetFqn,
                    DependencyKind = kind,
                    SourceFilePath = filePath
                });
            }

            ExtractGenericTypeArguments(baseEntry, sourceFqn, filePath, deps, typeFqnMap);
        }
        else
        {
            var targetName = baseEntry.Text;
            var targetFqn = typeFqnMap.TryGetValue(targetName, out var targetFqnVal) ? targetFqnVal : targetName;
            var kind = interfaceNames.Contains(targetName) ? DependencyKind.Implements : DependencyKind.Inherits;
            deps.Add(new DependencyEdge
            {
                SourceSymbol = sourceFqn,
                TargetSymbol = targetFqn,
                DependencyKind = kind,
                SourceFilePath = filePath
            });
        }
    }

    private static void ExtractGenericTypeArguments(Node genericNameNode, string sourceFqn, string filePath, List<DependencyEdge> deps, Dictionary<string, string> typeFqnMap)
    {
        var typeArgList = FindChildByType(genericNameNode, "type_argument_list");
        if (typeArgList is null)
        {
            return;
        }

        foreach (var arg in typeArgList.NamedChildren)
        {
            if (arg.Type == "identifier" || arg.Type == "type_identifier")
            {
                AddTypeDependency(arg.Text, sourceFqn, filePath, deps, typeFqnMap);
            }
            else if (arg.Type == "generic_name")
            {
                var innerName = GetGenericNameIdentifier(arg);
                if (innerName is not null)
                {
                    AddTypeDependency(innerName, sourceFqn, filePath, deps, typeFqnMap);
                }
                ExtractGenericTypeArguments(arg, sourceFqn, filePath, deps, typeFqnMap);
            }
        }
    }

    private static void ExtractGenericConstraintDependencies(Node typeDeclNode, string sourceFqn, string filePath, List<DependencyEdge> deps, Dictionary<string, string> typeFqnMap)
    {
        foreach (var child in typeDeclNode.Children)
        {
            if (child.Type == "type_parameter_constraints_clause")
            {
                foreach (var constraint in child.NamedChildren)
                {
                    if (constraint.Type == "type_parameter_constraint")
                    {
                        foreach (var typeNode in constraint.NamedChildren)
                        {
                            if (typeNode.Type == "identifier" || typeNode.Type == "type_identifier")
                            {
                                AddTypeDependency(typeNode.Text, sourceFqn, filePath, deps, typeFqnMap);
                            }
                            else if (typeNode.Type == "generic_name")
                            {
                                var genericName = GetGenericNameIdentifier(typeNode);
                                if (genericName is not null)
                                {
                                    AddTypeDependency(genericName, sourceFqn, filePath, deps, typeFqnMap);
                                }
                                ExtractGenericTypeArguments(typeNode, sourceFqn, filePath, deps, typeFqnMap);
                            }
                        }
                    }
                }
            }
        }
    }

    private static void ExtractAttributeDependencies(Node typeDeclNode, string sourceFqn, string filePath, List<DependencyEdge> deps, Dictionary<string, string> typeFqnMap)
    {
        CollectAttributeDependencies(typeDeclNode, sourceFqn, filePath, deps, typeFqnMap);
    }

    private static void CollectAttributeDependencies(Node node, string sourceFqn, string filePath, List<DependencyEdge> deps, Dictionary<string, string> typeFqnMap)
    {
        foreach (var child in node.Children)
        {
            if (child.Type == "attribute_list")
            {
                foreach (var attr in child.NamedChildren)
                {
                    if (attr.Type == "attribute")
                    {
                        var attrNameNode = attr.NamedChildren.FirstOrDefault();
                        if (attrNameNode is not null)
                        {
                            var attrName = attrNameNode.Text;
                            if (attrName.EndsWith("Attribute", StringComparison.Ordinal))
                            {
                                attrName = attrName[..^"Attribute".Length];
                            }
                            AddTypeDependency(attrName, sourceFqn, filePath, deps, typeFqnMap);
                        }
                    }
                }
            }
            else if (!TypeDeclNodeTypes.Contains(child.Type))
            {
                CollectAttributeDependencies(child, sourceFqn, filePath, deps, typeFqnMap);
            }
        }
    }

    private static void ExtractContainsDependencies(Node typeDeclNode, string sourceFqn, string filePath, List<DependencyEdge> deps, Dictionary<string, string> typeFqnMap)
    {
        var declList = FindChildByType(typeDeclNode, "declaration_list");
        if (declList is null)
        {
            return;
        }

        foreach (var child in declList.NamedChildren)
        {
            if (TypeDeclNodeTypes.Contains(child.Type))
            {
                var childNameNode = child.GetChildForField("name");
                if (childNameNode is not null)
                {
                    var childName = childNameNode.Text;
                    var childFqn = typeFqnMap.TryGetValue(childName, out var fqnVal) ? fqnVal : $"{sourceFqn}.{childName}";
                    deps.Add(new DependencyEdge
                    {
                        SourceSymbol = sourceFqn,
                        TargetSymbol = childFqn,
                        DependencyKind = DependencyKind.Contains,
                        SourceFilePath = filePath
                    });
                }
            }
        }
    }

    private static void ExtractTypeUsageDependencies(Node typeDeclNode, string sourceFqn, string filePath, List<DependencyEdge> deps, Dictionary<string, string> typeFqnMap)
    {
        var usedTypes = new HashSet<string>(StringComparer.Ordinal);
        CollectTypeReferences(typeDeclNode, usedTypes);

        deps.AddRange(usedTypes.Select(usedType =>
        {
            var targetFqn = typeFqnMap.TryGetValue(usedType, out var fqnVal) ? fqnVal : usedType;
            return new DependencyEdge
            {
                SourceSymbol = sourceFqn,
                TargetSymbol = targetFqn,
                DependencyKind = DependencyKind.Uses,
                SourceFilePath = filePath
            };
        }));
    }

    private static void CollectTypeReferences(Node node, HashSet<string> usedTypes)
    {
        switch (node.Type)
        {
            case "type_identifier":
                AddIfUserDefinedType(node, node.Text, usedTypes);
                break;
            case "generic_name":
                ExtractGenericNameTypeReferences(node, usedTypes);
                break;
            case "variable_declaration":
                ExtractTypeFromVariableDeclaration(node, usedTypes);
                break;
            case "parameter":
                ExtractTypeFromParameter(node, usedTypes);
                break;
            case "method_declaration":
                ExtractTypeFromMethodReturn(node, usedTypes);
                break;
            case "property_declaration":
                ExtractTypeFromProperty(node, usedTypes);
                break;
        }

        foreach (var child in node.NamedChildren)
        {
            CollectTypeReferences(child, usedTypes);
        }
    }

    private static void ExtractGenericNameTypeReferences(Node genericNameNode, HashSet<string> usedTypes)
    {
        var nameIdentifier = GetGenericNameIdentifier(genericNameNode);
        if (nameIdentifier is not null)
        {
            AddIfUserDefinedType(genericNameNode, nameIdentifier, usedTypes);
        }

        var typeArgList = FindChildByType(genericNameNode, "type_argument_list");
        if (typeArgList is null)
        {
            return;
        }

        foreach (var arg in typeArgList.NamedChildren)
        {
            if (arg.Type == "identifier" || arg.Type == "type_identifier")
            {
                AddIfUserDefinedType(arg, arg.Text, usedTypes);
            }
            else if (arg.Type == "generic_name")
            {
                ExtractGenericNameTypeReferences(arg, usedTypes);
            }
        }
    }

    private static void ExtractTypeFromVariableDeclaration(Node varDecl, HashSet<string> usedTypes)
    {
        var typeNode = varDecl.GetChildForField("type");
        if (typeNode is not null)
        {
            AddTypeFromNode(typeNode, usedTypes);
        }
    }

    private static void ExtractTypeFromParameter(Node param, HashSet<string> usedTypes)
    {
        var typeNode = param.GetChildForField("type");
        if (typeNode is not null)
        {
            AddTypeFromNode(typeNode, usedTypes);
        }
    }

    private static void ExtractTypeFromMethodReturn(Node methodDecl, HashSet<string> usedTypes)
    {
        var returnTypeNode = methodDecl.GetChildForField("returns");
        if (returnTypeNode is not null)
        {
            AddTypeFromNode(returnTypeNode, usedTypes);
        }
    }

    private static void ExtractTypeFromProperty(Node propDecl, HashSet<string> usedTypes)
    {
        var typeNode = propDecl.GetChildForField("type");
        if (typeNode is not null)
        {
            AddTypeFromNode(typeNode, usedTypes);
        }
    }

    private static void AddTypeFromNode(Node typeNode, HashSet<string> usedTypes)
    {
        if (typeNode.Type == "generic_name")
        {
            ExtractGenericNameTypeReferences(typeNode, usedTypes);
        }
        else
        {
            AddIfUserDefinedType(typeNode, typeNode.Text, usedTypes);
        }
    }

    private static void AddIfUserDefinedType(Node typeNode, string typeName, HashSet<string> usedTypes)
    {
        if (typeName.Length == 0 || BclTypes.Contains(typeName))
        {
            return;
        }

        if (typeNode.Parent is not null && SkipParentTypes.Contains(typeNode.Parent.Type))
        {
            return;
        }

        usedTypes.Add(typeName);
    }

    private static void AddTypeDependency(string typeName, string sourceFqn, string filePath, List<DependencyEdge> deps, Dictionary<string, string> typeFqnMap)
    {
        if (string.IsNullOrEmpty(typeName) || BclTypes.Contains(typeName))
        {
            return;
        }

        var targetFqn = typeFqnMap.TryGetValue(typeName, out var fqnVal) ? fqnVal : typeName;
        deps.Add(new DependencyEdge
        {
            SourceSymbol = sourceFqn,
            TargetSymbol = targetFqn,
            DependencyKind = DependencyKind.Uses,
            SourceFilePath = filePath
        });
    }

    private static string? GetGenericNameIdentifier(Node genericNameNode)
    {
        foreach (var child in genericNameNode.NamedChildren)
        {
            if (child.Type == "identifier")
            {
                return child.Text;
            }
        }
        return null;
    }

    private static void ExtractUsingDependency(Node usingNode, string fileFqn, string filePath, List<DependencyEdge> deps)
    {
        var nameChild = usingNode.NamedChildren.FirstOrDefault();
        if (nameChild is null)
        {
            return;
        }

        deps.Add(new DependencyEdge
        {
            SourceSymbol = fileFqn,
            TargetSymbol = nameChild.Text,
            DependencyKind = DependencyKind.Imports,
            SourceFilePath = filePath
        });
    }

    private static Node? FindChildByType(Node node, string type)
    {
        return node.Children.FirstOrDefault(child => child.Type == type);
    }
}
