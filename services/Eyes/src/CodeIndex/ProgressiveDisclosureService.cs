namespace Services.CodeIndex;

[Register]
public sealed partial class ProgressiveDisclosureService : IProgressiveDisclosure
{
    private readonly ICodeIndexer _indexer;
    [Inject] private readonly ILogger<ProgressiveDisclosureService>? _logger;
    private readonly IFileSystem _fs;

    public ProgressiveDisclosureService(ICodeIndexer indexer, IFileSystem fs, ILogger<ProgressiveDisclosureService>? logger = null)
    {
        _indexer = indexer ?? throw new ArgumentNullException(nameof(indexer));
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _logger = logger;
    }

    public async Task<DisclosureResult> DiscloseAsync(string query, DisclosureLevel level, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var searchResult = await _indexer.Searcher.SearchAsync(query, ct).ConfigureAwait(false);
        if (searchResult.Items.Count == 0)
        {
            return EmptyResult(query, level);
        }

        return level switch
        {
            DisclosureLevel.Index => await BuildIndexLevelAsync(query, searchResult.Items, ct).ConfigureAwait(false),
            DisclosureLevel.Relationships => await BuildRelationshipsLevelAsync(query, searchResult.Items, ct).ConfigureAwait(false),
            DisclosureLevel.Source => await BuildSourceLevelAsync(query, searchResult.Items, ct).ConfigureAwait(false),
            _ => await BuildIndexLevelAsync(query, searchResult.Items, ct).ConfigureAwait(false)
        };
    }

    public async Task<DisclosureResult> ExpandAsync(DisclosureResult previous, CancellationToken ct)
    {
        if (!previous.HasMoreDetails)
        {
            return previous;
        }

        var nextLevel = previous.Level + 1;
        return await DiscloseAsync(previous.Query, nextLevel, ct).ConfigureAwait(false);
    }

    private static DisclosureResult EmptyResult(string query, DisclosureLevel level)
    {
        return new DisclosureResult
        {
            Query = query,
            Level = level,
            FormattedContent = L.T(StringKey.ProgressiveDisclosureNoSymbolsFound, query),
            Symbols = [],
            EstimatedTokens = 10
        };
    }

    private static Task<DisclosureResult> BuildIndexLevelAsync(string query, IReadOnlyList<SymbolInfo> symbols, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.Append(L.T(StringKey.ProgressiveDisclosureSymbolIndex));
        sb.Append(query);
        sb.AppendLine();
        sb.AppendLine();

        sb.Append(string.Join(Environment.NewLine,
            symbols.Take(20).Select(FormatSymbolIndex)));
        sb.AppendLine();

        var content = sb.ToString();

        return Task.FromResult(new DisclosureResult
        {
            Query = query,
            Level = DisclosureLevel.Index,
            FormattedContent = content,
            Symbols = symbols.Take(20).ToList(),
            EstimatedTokens = EstimateTokens(content)
        });
    }

    private async Task<DisclosureResult> BuildRelationshipsLevelAsync(string query, IReadOnlyList<SymbolInfo> symbols, CancellationToken ct)
    {
        var indexResult = await BuildIndexLevelAsync(query, symbols, ct).ConfigureAwait(false);
        var sb = new StringBuilder(indexResult.FormattedContent);
        sb.AppendLine();
        sb.AppendLine(L.T(StringKey.ProgressiveDisclosureCallGraph));
        sb.AppendLine();

        var callers = new List<CallEdge>();
        var callees = new List<CallEdge>();
        var inheritors = new List<DependencyEdge>();
        var dependencies = new List<DependencyEdge>();

        // 并行查询每个符号的4种关系图，收集结果后串行格式化
        var symbolResults = new (CallEdge[] Callers, CallEdge[] Callees, DependencyEdge[] Inheritors, DependencyEdge[] Dependencies)[symbols.Take(5).Count()];
        var symbolList = symbols.Take(5).ToArray();

        var symbolTasks = symbolList.Select(async (s, idx) =>
        {
            try
            {
                var callersTask = _indexer.CallGraph.GetCallersAsync(s.Name, ct);
                var calleesTask = _indexer.CallGraph.GetCalleesAsync(s.Name, ct);
                var inheritorsTask = _indexer.DependencyGraph.GetInheritorsAsync(s.Name, ct);
                var depsTask = _indexer.DependencyGraph.GetDependenciesAsync(s.Name, ct);

                await Task.WhenAll(callersTask, calleesTask, inheritorsTask, depsTask).ConfigureAwait(false);

                symbolResults[idx] = (
                    (await callersTask.ConfigureAwait(false)).ToArray(),
                    (await calleesTask.ConfigureAwait(false)).ToArray(),
                    (await inheritorsTask.ConfigureAwait(false)).ToArray(),
                    (await depsTask.ConfigureAwait(false)).ToArray()
                );
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, L.T(StringKey.ProgressiveDisclosureRelationshipFailed), s.Name);
                symbolResults[idx] = ([], [], [], []);
            }
        }).ToArray();

        await Task.WhenAll(symbolTasks).ConfigureAwait(false);

        // 串行格式化输出（StringBuilder 非线程安全）
        for (var i = 0; i < symbolList.Length; i++)
        {
            var s = symbolList[i];
            var (symCallers, symCallees, symInheritors, symDeps) = symbolResults[i];

            if (symCallers.Length > 0)
            {
                sb.Append("### ");
                sb.Append(s.Name);
                sb.Append(L.T(StringKey.ProgressiveDisclosureCallers));
                foreach (var caller in symCallers.Take(10))
                {
                    sb.Append("- `");
                    sb.Append(caller.CallerSymbol);
                    sb.Append("` (");
                    sb.Append(caller.CallSiteFilePath);
                    sb.Append(':');
                    sb.Append(caller.CallSiteLine);
                    sb.AppendLine(")");
                }

                callers.AddRange(symCallers);
            }

            if (symCallees.Length > 0)
            {
                sb.Append("### ");
                sb.Append(s.Name);
                sb.Append(L.T(StringKey.ProgressiveDisclosureCallees));
                foreach (var callee in symCallees.Take(10))
                {
                    sb.Append("- `");
                    sb.Append(callee.CalleeSymbol);
                    sb.Append("` (");
                    sb.Append(callee.CallSiteFilePath);
                    sb.Append(':');
                    sb.Append(callee.CallSiteLine);
                    sb.AppendLine(")");
                }

                callees.AddRange(symCallees);
            }

            if (symInheritors.Length > 0)
            {
                sb.Append("### ");
                sb.Append(s.Name);
                sb.Append(L.T(StringKey.ProgressiveDisclosureInheritors));
                foreach (var inh in symInheritors.Take(10))
                {
                    sb.Append("- `");
                    sb.Append(inh.TargetSymbol);
                    sb.Append("` (");
                    sb.Append(inh.DependencyKind);
                    sb.AppendLine(")");
                }

                inheritors.AddRange(symInheritors);
            }

            if (symDeps.Length > 0)
            {
                sb.Append("### ");
                sb.Append(s.Name);
                sb.Append(L.T(StringKey.ProgressiveDisclosureDependencies));
                foreach (var dep in symDeps.Take(10))
                {
                    sb.Append("- `");
                    sb.Append(dep.TargetSymbol);
                    sb.Append("` (");
                    sb.Append(dep.DependencyKind);
                    sb.AppendLine(")");
                }

                dependencies.AddRange(symDeps);
            }
        }

        var content = sb.ToString();

        return new DisclosureResult
        {
            Query = query,
            Level = DisclosureLevel.Relationships,
            FormattedContent = content,
            Symbols = indexResult.Symbols,
            Callers = callers,
            Callees = callees,
            Inheritors = inheritors,
            Dependencies = dependencies,
            EstimatedTokens = EstimateTokens(content)
        };
    }

    private async Task<DisclosureResult> BuildSourceLevelAsync(string query, IReadOnlyList<SymbolInfo> symbols, CancellationToken ct)
    {
        var relResult = await BuildRelationshipsLevelAsync(query, symbols, ct).ConfigureAwait(false);
        var sb = new StringBuilder(relResult.FormattedContent);
        sb.AppendLine();
        sb.AppendLine(L.T(StringKey.ProgressiveDisclosureSourceCode));
        sb.AppendLine();

        var snippets = new List<SourceSnippet>();

        foreach (var s in symbols.Take(3))
        {
            try
            {
                if (!_fs.FileExists(s.FilePath))
                {
                    continue;
                }

                var lines = await _fs.ReadAllLinesAsync(s.FilePath, ct).ConfigureAwait(false);
                var startLine = Math.Max(0, s.StartLine - 1);
                var endLine = Math.Min(lines.Length, s.EndLine);

                if (startLine >= lines.Length)
                {
                    continue;
                }

                var fileContent = string.Join("\n", lines[startLine..endLine]);
                snippets.Add(new SourceSnippet
                {
                    FilePath = s.FilePath,
                    StartLine = s.StartLine,
                    EndLine = s.EndLine,
                    Content = fileContent,
                    SymbolName = s.Name
                });

                sb.Append("### ");
                sb.Append(s.Name);
                sb.Append(" (");
                sb.Append(s.FilePath);
                sb.Append(':');
                sb.Append(s.StartLine);
                sb.Append('-');
                sb.Append(s.EndLine);
                sb.AppendLine(")");
                sb.AppendLine("```csharp");
                sb.AppendLine(fileContent);
                sb.AppendLine("```");
                sb.AppendLine();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, L.T(StringKey.ProgressiveDisclosureReadSourceFailed), s.FilePath);
            }
        }

        var finalContent = sb.ToString();

        return new DisclosureResult
        {
            Query = query,
            Level = DisclosureLevel.Source,
            FormattedContent = finalContent,
            Symbols = relResult.Symbols,
            Callers = relResult.Callers,
            Callees = relResult.Callees,
            Inheritors = relResult.Inheritors,
            Dependencies = relResult.Dependencies,
            SourceSnippets = snippets,
            EstimatedTokens = EstimateTokens(finalContent)
        };
    }

    private static int EstimateTokens(string content)
    {
        return Math.Max(1, content.Length / 4);
    }

    private static string FormatSymbolIndex(SymbolInfo s)
    {
        var parent = string.IsNullOrEmpty(s.ParentSymbol) ? "" : $" (in {s.ParentSymbol})";
        return $"{s.Kind} **{s.Name}**{parent} → `{s.FilePath}:{s.StartLine}-{s.EndLine}`";
    }
}
