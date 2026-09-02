namespace Tools.Handlers;

/// <summary>
/// LSP 工具处理器 — 语言服务器协议集成，提供代码智能查询能力
/// 对齐 TS 原版 tools/LSPTool/LSPTool.ts（9 种操作）
/// 依赖 ILspService 基础设施（services/Eyes/src/Lsp/）
/// </summary>
[McpToolDispatch(ToolCategory.Lsp)]
public sealed partial class LspToolHandlers
{
    private readonly ILspService? _lspService;
    private readonly ILogger<LspToolHandlers>? _logger;

    public LspToolHandlers(ILspService? lspService = null, ILogger<LspToolHandlers>? logger = null)
    {
        _lspService = lspService;
        _logger = logger;
    }

    /// <summary>跳转到定义 — 返回定义位置列表</summary>
    [McpTool("lsp_go_to_definition", "跳转到符号定义位置,返回文件路径/行号/列号", "lsp", ConcurrencySafe = true)]
    public async Task<ToolResult> GoToDefinitionAsync(
        [McpToolParameter("文件路径（绝对或相对）", Required = true)] string filePath,
        [McpToolParameter("行号（1-based）", Required = true)] int line,
        [McpToolParameter("列号（1-based）", Required = true)] int character,
        CancellationToken ct = default)
    {
        if (_lspService is null)
        {
            return ToolResultBuilder.Error().WithText("LSP 服务未配置，请在 settings.json 中配置 LSP 服务器").Build();
        }

        try
        {
            var locations = await _lspService.GotoDefinitionAsync(filePath, line, character, ct).ConfigureAwait(false);
            return ToolResultBuilder.Success().WithText(FormatLocations("跳转到定义", locations)).Build();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "LSP 跳转定义失败: {FilePath}:{Line}:{Character}", filePath, line, character);
            return ToolResultBuilder.Error().WithText($"LSP 跳转定义失败: {ex.Message}").Build();
        }
    }

    /// <summary>查找引用 — 返回所有引用位置列表</summary>
    [McpTool("lsp_find_references", "查找符号所有引用位置,返回文件路径/行号/列号列表", "lsp", ConcurrencySafe = true)]
    public async Task<ToolResult> FindReferencesAsync(
        [McpToolParameter("文件路径（绝对或相对）", Required = true)] string filePath,
        [McpToolParameter("行号（1-based）", Required = true)] int line,
        [McpToolParameter("列号（1-based）", Required = true)] int character,
        CancellationToken ct = default)
    {
        if (_lspService is null)
        {
            return ToolResultBuilder.Error().WithText("LSP 服务未配置，请在 settings.json 中配置 LSP 服务器").Build();
        }

        try
        {
            var locations = await _lspService.FindReferencesAsync(filePath, line, character, ct).ConfigureAwait(false);
            return ToolResultBuilder.Success().WithText(FormatLocations("查找引用", locations)).Build();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "LSP 查找引用失败: {FilePath}:{Line}:{Character}", filePath, line, character);
            return ToolResultBuilder.Error().WithText($"LSP 查找引用失败: {ex.Message}").Build();
        }
    }

    /// <summary>悬停提示 — 返回符号的类型/文档信息</summary>
    [McpTool("lsp_hover", "获取符号悬停提示,包含类型签名和文档注释", "lsp", ConcurrencySafe = true)]
    public async Task<ToolResult> HoverAsync(
        [McpToolParameter("文件路径（绝对或相对）", Required = true)] string filePath,
        [McpToolParameter("行号（1-based）", Required = true)] int line,
        [McpToolParameter("列号（1-based）", Required = true)] int character,
        CancellationToken ct = default)
    {
        if (_lspService is null)
        {
            return ToolResultBuilder.Error().WithText("LSP 服务未配置，请在 settings.json 中配置 LSP 服务器").Build();
        }

        try
        {
            var hover = await _lspService.HoverAsync(filePath, line, character, ct).ConfigureAwait(false);
            if (hover is null)
            {
                return ToolResultBuilder.Success().WithText("无悬停信息").Build();
            }

            var content = hover.Contents.HasValue
                ? hover.Contents.Value.GetRawText()
                : "(空)";
            return ToolResultBuilder.Success().WithText($"悬停信息:\n{content}").Build();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "LSP 悬停失败: {FilePath}:{Line}:{Character}", filePath, line, character);
            return ToolResultBuilder.Error().WithText($"LSP 悬停失败: {ex.Message}").Build();
        }
    }

    /// <summary>代码补全 — 返回补全建议列表</summary>
    [McpTool("lsp_get_completions", "获取代码补全建议列表,包含标签/类型/插入文本", "lsp", ConcurrencySafe = true)]
    public async Task<ToolResult> GetCompletionsAsync(
        [McpToolParameter("文件路径（绝对或相对）", Required = true)] string filePath,
        [McpToolParameter("行号（1-based）", Required = true)] int line,
        [McpToolParameter("列号（1-based）", Required = true)] int character,
        CancellationToken ct = default)
    {
        if (_lspService is null)
        {
            return ToolResultBuilder.Error().WithText("LSP 服务未配置，请在 settings.json 中配置 LSP 服务器").Build();
        }

        try
        {
            var completions = await _lspService.GetCompletionsAsync(filePath, line, character, ct).ConfigureAwait(false);
            if (completions.Count == 0)
            {
                return ToolResultBuilder.Success().WithText("无补全建议").Build();
            }

            var sb = new StringBuilder(256);
            sb.AppendLine($"共 {completions.Count} 个补全建议:");
            foreach (var item in completions)
            {
                sb.Append("  ").Append(item.Label);
                if (item.Detail is not null)
                {
                    sb.Append(" — ").Append(item.Detail);
                }
                if (item.InsertText is not null && item.InsertText != item.Label)
                {
                    sb.Append(" [insert: ").Append(item.InsertText).Append(']');
                }
                sb.AppendLine();
            }

            return ToolResultBuilder.Success().WithText(sb.ToString()).Build();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "LSP 补全失败: {FilePath}:{Line}:{Character}", filePath, line, character);
            return ToolResultBuilder.Error().WithText($"LSP 补全失败: {ex.Message}").Build();
        }
    }

    /// <summary>文档符号 — 返回文件中所有符号列表</summary>
    [McpTool("lsp_document_symbols", "获取文件中所有符号（类/方法/字段等）,含位置和类型", "lsp", ConcurrencySafe = true)]
    public async Task<ToolResult> GetDocumentSymbolsAsync(
        [McpToolParameter("文件路径（绝对或相对）", Required = true)] string filePath,
        CancellationToken ct = default)
    {
        if (_lspService is null)
        {
            return ToolResultBuilder.Error().WithText("LSP 服务未配置，请在 settings.json 中配置 LSP 服务器").Build();
        }

        try
        {
            var symbols = await _lspService.GetDocumentSymbolsAsync(filePath, ct).ConfigureAwait(false);
            if (symbols.Count == 0)
            {
                return ToolResultBuilder.Success().WithText("无文档符号").Build();
            }

            var sb = new StringBuilder(256);
            sb.AppendLine($"共 {symbols.Count} 个文档符号:");
            AppendDocumentSymbols(sb, symbols, indent: 0);

            return ToolResultBuilder.Success().WithText(sb.ToString()).Build();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "LSP 文档符号失败: {FilePath}", filePath);
            return ToolResultBuilder.Error().WithText($"LSP 文档符号失败: {ex.Message}").Build();
        }
    }

    /// <summary>工作区符号搜索 — 按名称搜索整个工作区的符号</summary>
    [McpTool("lsp_workspace_symbols", "按名称搜索工作区中所有符号,返回符号名/类型/位置", "lsp", ConcurrencySafe = true)]
    public async Task<ToolResult> SearchWorkspaceSymbolsAsync(
        [McpToolParameter("搜索查询（符号名称片段）", Required = true)] string query,
        CancellationToken ct = default)
    {
        if (_lspService is null)
        {
            return ToolResultBuilder.Error().WithText("LSP 服务未配置，请在 settings.json 中配置 LSP 服务器").Build();
        }

        try
        {
            var symbols = await _lspService.SearchWorkspaceSymbolsAsync(query, ct).ConfigureAwait(false);
            if (symbols.Count == 0)
            {
                return ToolResultBuilder.Success().WithText($"未找到匹配 '{query}' 的符号").Build();
            }

            var sb = new StringBuilder(256);
            sb.AppendLine($"共 {symbols.Count} 个匹配符号:");
            foreach (var sym in symbols)
            {
                sb.Append("  ").Append(sym.Name);
                if (sym.ContainerName is not null)
                {
                    sb.Append(" (in ").Append(sym.ContainerName).Append(')');
                }
                sb.Append(" — ").Append(FormatLocation(sym.Location));
                sb.AppendLine();
            }

            return ToolResultBuilder.Success().WithText(sb.ToString()).Build();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "LSP 工作区符号搜索失败: {Query}", query);
            return ToolResultBuilder.Error().WithText($"LSP 工作区符号搜索失败: {ex.Message}").Build();
        }
    }

    /// <summary>跳转到实现 — 返回接口/抽象方法的实现位置</summary>
    [McpTool("lsp_go_to_implementation", "跳转到接口/抽象方法的实现位置", "lsp", ConcurrencySafe = true)]
    public async Task<ToolResult> GoToImplementationAsync(
        [McpToolParameter("文件路径（绝对或相对）", Required = true)] string filePath,
        [McpToolParameter("行号（1-based）", Required = true)] int line,
        [McpToolParameter("列号（1-based）", Required = true)] int character,
        CancellationToken ct = default)
    {
        if (_lspService is null)
        {
            return ToolResultBuilder.Error().WithText("LSP 服务未配置，请在 settings.json 中配置 LSP 服务器").Build();
        }

        try
        {
            var locations = await _lspService.GotoImplementationAsync(filePath, line, character, ct).ConfigureAwait(false);
            return ToolResultBuilder.Success().WithText(FormatLocations("跳转到实现", locations)).Build();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "LSP 跳转实现失败: {FilePath}:{Line}:{Character}", filePath, line, character);
            return ToolResultBuilder.Error().WithText($"LSP 跳转实现失败: {ex.Message}").Build();
        }
    }

    /// <summary>准备调用层次 — 获取符号的调用层次入口</summary>
    [McpTool("lsp_prepare_call_hierarchy", "准备调用层次分析,返回调用层次项列表", "lsp", ConcurrencySafe = true)]
    public async Task<ToolResult> PrepareCallHierarchyAsync(
        [McpToolParameter("文件路径（绝对或相对）", Required = true)] string filePath,
        [McpToolParameter("行号（1-based）", Required = true)] int line,
        [McpToolParameter("列号（1-based）", Required = true)] int character,
        CancellationToken ct = default)
    {
        if (_lspService is null)
        {
            return ToolResultBuilder.Error().WithText("LSP 服务未配置，请在 settings.json 中配置 LSP 服务器").Build();
        }

        try
        {
            var items = await _lspService.PrepareCallHierarchyAsync(filePath, line, character, ct).ConfigureAwait(false);
            if (items.Count == 0)
            {
                return ToolResultBuilder.Success().WithText("无调用层次信息").Build();
            }

            var sb = new StringBuilder(128);
            sb.AppendLine($"共 {items.Count} 个调用层次项:");
            foreach (var item in items)
            {
                sb.Append("  ").Append(item.Name).Append(" — ").Append(FormatLocation(new LspLocation { Uri = item.Uri, Range = item.Range }));
                sb.AppendLine();
            }

            return ToolResultBuilder.Success().WithText(sb.ToString()).Build();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "LSP 准备调用层次失败: {FilePath}:{Line}:{Character}", filePath, line, character);
            return ToolResultBuilder.Error().WithText($"LSP 准备调用层次失败: {ex.Message}").Build();
        }
    }

    private static string FormatLocations(string label, List<LspLocation> locations)
    {
        if (locations.Count == 0)
        {
            return $"{label}: 无结果";
        }

        var sb = new StringBuilder(128);
        sb.AppendLine($"{label}: 共 {locations.Count} 个位置");
        foreach (var loc in locations)
        {
            sb.Append("  ").AppendLine(FormatLocation(loc));
        }

        return sb.ToString();
    }

    private static string FormatLocation(LspLocation location)
    {
        var uri = location.Uri;
        var path = uri.StartsWith("file://", StringComparison.Ordinal)
            ? Uri.UnescapeDataString(uri[7..])
            : uri;
        return $"{path}:{location.Range.Start.Line + 1}:{location.Range.Start.Character + 1}";
    }

    private static void AppendDocumentSymbols(StringBuilder sb, List<LspDocumentSymbol> symbols, int indent)
    {
        var prefix = new string(' ', indent * 2);
        foreach (var sym in symbols)
        {
            sb.Append(prefix).Append(sym.Name);
            if (sym.Detail is not null)
            {
                sb.Append(" — ").Append(sym.Detail);
            }
            sb.Append(" [kind=").Append(sym.Kind).Append(']');
            sb.AppendLine();

            if (sym.Children is not null && sym.Children.Count > 0)
            {
                AppendDocumentSymbols(sb, sym.Children, indent + 1);
            }
        }
    }
}
