
namespace Services.Lsp.ToolHandlers;

/// <summary>
/// LSP工具处理器 - 提供语言服务器协议功能
/// </summary>
[McpToolHandler(ToolCategory.Lsp, Optional = true)]
public class LspToolHandlers {
    private readonly ILspService _lspService;
    private readonly IFileOperationService _fileOperationService;

    public LspToolHandlers(ILspService lspService, IFileOperationService fileOperationService) {
        _lspService = lspService ?? throw new ArgumentNullException(nameof(lspService));
        _fileOperationService = fileOperationService ?? throw new ArgumentNullException(nameof(fileOperationService));
    }

    /// <summary>
    /// 跳转到定义
    /// </summary>
    [McpTool(CodeToolNameConstants.LspGotoDefinition, "Go to symbol definition location", "lsp")]
    public async Task<ToolResult> LspGotoDefinitionAsync(
        [McpToolParameter("File path")] string file_path,
        [McpToolParameter("Line number (1-based, consistent with editor)")] int line,
        [McpToolParameter("Character position (1-based, consistent with editor)")] int character,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(file_path)) {
            return McpResultBuilder.Error().WithText(L.T(StringKey.FilePathCannotBeEmpty)).Build();
        }

        var fileResult = await _fileOperationService.ReadFileAsync(file_path, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!fileResult.Success) {
            return McpResultBuilder.Error().WithText(L.T(StringKey.FileNotExist, file_path)).Build();
        }

        try {
            var locations = await _lspService.GotoDefinitionAsync(file_path, line - 1, character - 1, cancellationToken).ConfigureAwait(false);

            locations = await FilterLocationsGitIgnoredAsync(locations).ConfigureAwait(false);

            if (locations.Count == 0) {
                return McpResultBuilder.Success().WithText(L.T(StringKey.NoDefinitionFound)).Build();
            }

            var response = new System.Text.StringBuilder();
            response.AppendLine(L.T(StringKey.FoundDefinitionsCount, locations.Count));
            response.AppendLine();

            for (int i = 0; i < locations.Count; i++) {
                var loc = locations[i];
                var fileUri = loc.Uri;
                var filePath = UriToFilePath(fileUri);

                response.AppendLine($"{i + 1}. {filePath}");
                response.AppendLine($"   {L.T(StringKey.LabelRowCol, loc.Range.Start.Line + 1, loc.Range.Start.Character + 1)}");
                response.AppendLine();
            }

            return McpResultBuilder.Success().WithText(response.ToString()).Build();
        } catch (Exception ex) {
            return McpResultBuilder.Error().WithText(L.T(StringKey.LspError, ex.Message)).Build();
        }
    }

    /// <summary>
    /// 查找引用
    /// </summary>
    [McpTool(CodeToolNameConstants.LspFindReferences, "Find all references to a symbol", "lsp")]
    public async Task<ToolResult> LspFindReferencesAsync(
        [McpToolParameter("File path")] string file_path,
        [McpToolParameter("Line number (1-based, consistent with editor)")] int line,
        [McpToolParameter("Character position (1-based, consistent with editor)")] int character,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(file_path)) {
            return McpResultBuilder.Error().WithText(L.T(StringKey.FilePathCannotBeEmpty)).Build();
        }

        var fileResult = await _fileOperationService.ReadFileAsync(file_path, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!fileResult.Success) {
            return McpResultBuilder.Error().WithText(L.T(StringKey.FileNotExist, file_path)).Build();
        }

        try {
            var locations = await _lspService.FindReferencesAsync(file_path, line - 1, character - 1, cancellationToken).ConfigureAwait(false);

            locations = await FilterLocationsGitIgnoredAsync(locations).ConfigureAwait(false);

            if (locations.Count == 0) {
                return McpResultBuilder.Success().WithText(L.T(StringKey.NoReferencesFound)).Build();
            }

            var response = new System.Text.StringBuilder();
            response.AppendLine(L.T(StringKey.FoundReferencesCountLsp, locations.Count));
            response.AppendLine();

            // 按文件分组
            var grouped = locations.GroupBy(l => l.Uri).ToList();

            foreach (var group in grouped) {
                var fileUri = group.Key;
                var path = UriToFilePath(fileUri);

                response.AppendLine($"{ObjectSymbol.File.ToValue()} {path}");

                foreach (var loc in group.OrderBy(l => l.Range.Start.Line)) {
                    response.AppendLine($"   {L.T(StringKey.LabelRowColShort, loc.Range.Start.Line + 1, loc.Range.Start.Character + 1)}");
                }

                response.AppendLine();
            }

            return McpResultBuilder.Success().WithText(response.ToString()).Build();
        } catch (Exception ex) {
            return McpResultBuilder.Error().WithText(L.T(StringKey.LspError, ex.Message)).Build();
        }
    }

    /// <summary>
    /// 悬停提示
    /// </summary>
    [McpTool(CodeToolNameConstants.LspHover, "Get hover information", "lsp")]
    public async Task<ToolResult> LspHoverAsync(
        [McpToolParameter("File path")] string file_path,
        [McpToolParameter("Line number (1-based, consistent with editor)")] int line,
        [McpToolParameter("Character position (1-based, consistent with editor)")] int character,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(file_path)) {
            return McpResultBuilder.Error().WithText(L.T(StringKey.FilePathCannotBeEmpty)).Build();
        }

        var fileResult = await _fileOperationService.ReadFileAsync(file_path, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!fileResult.Success) {
            return McpResultBuilder.Error().WithText(L.T(StringKey.FileNotExist, file_path)).Build();
        }

        try {
            var hover = await _lspService.HoverAsync(file_path, line - 1, character - 1, cancellationToken).ConfigureAwait(false);

            if (hover?.Contents == null) {
                return McpResultBuilder.Success().WithText(L.T(StringKey.NoHoverInfo)).Build();
            }

            var content = FormatHoverContents(hover.Contents);

            var response = new System.Text.StringBuilder();
            response.AppendLine($"{ObjectSymbol.DiamondFilled.ToValue()} {L.T(StringKey.HoverInfo)}");
            response.AppendLine();
            response.AppendLine(content);

            return McpResultBuilder.Success().WithText(response.ToString()).Build();
        } catch (Exception ex) {
            return McpResultBuilder.Error().WithText(L.T(StringKey.LspError, ex.Message)).Build();
        }
    }

    /// <summary>
    /// 代码补全
    /// </summary>
    [McpTool(CodeToolNameConstants.LspCompletion, "Get code completion suggestions", "lsp")]
    public async Task<ToolResult> LspCompletionAsync(
        [McpToolParameter("File path")] string file_path,
        [McpToolParameter("Line number (1-based, consistent with editor)")] int line,
        [McpToolParameter("Character position (1-based, consistent with editor)")] int character,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(file_path)) {
            return McpResultBuilder.Error().WithText(L.T(StringKey.FilePathCannotBeEmpty)).Build();
        }

        var fileResult = await _fileOperationService.ReadFileAsync(file_path, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!fileResult.Success) {
            return McpResultBuilder.Error().WithText(L.T(StringKey.FileNotExist, file_path)).Build();
        }

        try {
            var completions = await _lspService.GetCompletionsAsync(file_path, line - 1, character - 1, cancellationToken).ConfigureAwait(false);

            if (completions.Count == 0) {
                return McpResultBuilder.Success().WithText(L.T(StringKey.NoCompletionSuggestions)).Build();
            }

            var response = new System.Text.StringBuilder();
            response.AppendLine(L.T(StringKey.FoundCompletionCount, completions.Count));
            response.AppendLine();

            for (int i = 0; i < Math.Min(completions.Count, 20); i++) {
                var item = completions[i];
                var kindIcon = GetCompletionKindIcon(item.Kind);

                response.AppendLine($"{i + 1}. {kindIcon} {item.Label}");

                if (!string.IsNullOrEmpty(item.Detail)) {
                    response.AppendLine($"   {L.T(StringKey.LabelDetail, item.Detail)}");
                }

                if (item.Documentation != null) {
                    var doc = FormatDocumentation(item.Documentation);
                    if (!string.IsNullOrEmpty(doc)) {
                        response.AppendLine($"   {L.T(StringKey.LabelDocumentation, doc[..Math.Min(100, doc.Length)])}");
                    }
                }

                response.AppendLine();
            }

            if (completions.Count > 20) {
                response.AppendLine(L.T(StringKey.MoreSuggestions, completions.Count - 20));
            }

            return McpResultBuilder.Success().WithText(response.ToString()).Build();
        } catch (Exception ex) {
            return McpResultBuilder.Error().WithText(L.T(StringKey.LspError, ex.Message)).Build();
        }
    }

    /// <summary>
    /// 文档符号
    /// </summary>
    [McpTool(CodeToolNameConstants.LspDocumentSymbols, "Get document symbol list", "lsp")]
    public async Task<ToolResult> LspDocumentSymbolsAsync(
        [McpToolParameter("File path")] string file_path,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(file_path)) {
            return McpResultBuilder.Error().WithText(L.T(StringKey.FilePathCannotBeEmpty)).Build();
        }

        var fileResult = await _fileOperationService.ReadFileAsync(file_path, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!fileResult.Success) {
            return McpResultBuilder.Error().WithText(L.T(StringKey.FileNotExist, file_path)).Build();
        }

        try {
            var symbols = await _lspService.GetDocumentSymbolsAsync(file_path, cancellationToken).ConfigureAwait(false);

            if (symbols.Count == 0) {
                return McpResultBuilder.Success().WithText(L.T(StringKey.NoDocumentSymbols)).Build();
            }

            var response = new System.Text.StringBuilder();
            response.AppendLine($"{ObjectSymbol.List.ToValue()} {L.T(StringKey.DocumentSymbolsList, symbols.Count)}");
            response.AppendLine();

            foreach (var symbol in symbols) {
                FormatSymbol(response, symbol, 0);
            }

            return McpResultBuilder.Success().WithText(response.ToString()).Build();
        } catch (Exception ex) {
            return McpResultBuilder.Error().WithText(L.T(StringKey.LspError, ex.Message)).Build();
        }
    }

    /// <summary>
    /// 工作区符号搜索
    /// </summary>
    [McpTool(CodeToolNameConstants.LspWorkspaceSymbol, "Search symbols in workspace", "lsp")]
    public async Task<ToolResult> LspWorkspaceSymbolAsync(
        [McpToolParameter("Search query")] string query,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(query)) {
            return McpResultBuilder.Error().WithText(L.T(StringKey.QueryCannotBeEmpty)).Build();
        }

        try {
            var symbols = await _lspService.SearchWorkspaceSymbolsAsync(query, cancellationToken).ConfigureAwait(false);

            symbols = await FilterSymbolsGitIgnoredAsync(symbols).ConfigureAwait(false);

            if (symbols.Count == 0) {
                return McpResultBuilder.Success().WithText(L.T(StringKey.NoMatchingSymbolsLsp, query)).Build();
            }

            var response = new System.Text.StringBuilder();
            response.AppendLine($"{ObjectSymbol.Search.ToValue()} {L.T(StringKey.WorkspaceSymbolResults, symbols.Count)}");
            response.AppendLine();

            for (int i = 0; i < Math.Min(symbols.Count, 30); i++) {
                var symbol = symbols[i];
                var kindIcon = GetSymbolKindIcon(symbol.Kind);
                var filePath = UriToFilePath(symbol.Location.Uri);

                response.AppendLine($"{i + 1}. {kindIcon} {symbol.Name}");

                if (!string.IsNullOrEmpty(symbol.ContainerName)) {
                    response.AppendLine($"   {L.T(StringKey.LabelContainer, symbol.ContainerName)}");
                }

                response.AppendLine($"   {L.T(StringKey.LabelLocation, filePath, symbol.Location.Range.Start.Line + 1)}");
                response.AppendLine();
            }

            if (symbols.Count > 30) {
                response.AppendLine(L.T(StringKey.MoreResultsLsp, symbols.Count - 30));
            }

            return McpResultBuilder.Success().WithText(response.ToString()).Build();
        } catch (Exception ex) {
            return McpResultBuilder.Error().WithText(L.T(StringKey.LspError, ex.Message)).Build();
        }
    }

    /// <summary>
    /// 跳转到实现
    /// </summary>
    [McpTool(CodeToolNameConstants.LspGotoImplementation, "Go to symbol implementation location", "lsp")]
    public async Task<ToolResult> LspGotoImplementationAsync(
        [McpToolParameter("File path")] string file_path,
        [McpToolParameter("Line number (1-based, consistent with editor)")] int line,
        [McpToolParameter("Character position (1-based, consistent with editor)")] int character,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(file_path)) {
            return McpResultBuilder.Error().WithText(L.T(StringKey.FilePathCannotBeEmpty)).Build();
        }

        var fileResult = await _fileOperationService.ReadFileAsync(file_path, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!fileResult.Success) {
            return McpResultBuilder.Error().WithText(L.T(StringKey.FileNotExist, file_path)).Build();
        }

        try {
            var locations = await _lspService.GotoImplementationAsync(file_path, line - 1, character - 1, cancellationToken).ConfigureAwait(false);

            locations = await FilterLocationsGitIgnoredAsync(locations).ConfigureAwait(false);

            if (locations.Count == 0) {
                return McpResultBuilder.Success().WithText(L.T(StringKey.NoImplementationFound)).Build();
            }

            var response = new System.Text.StringBuilder();
            response.AppendLine(L.T(StringKey.FoundImplementationsCount, locations.Count));
            response.AppendLine();

            for (int i = 0; i < locations.Count; i++) {
                var loc = locations[i];
                var fileUri = loc.Uri;
                var displayPath = UriToFilePath(fileUri);

                response.AppendLine($"{i + 1}. {displayPath}");
                response.AppendLine($"   {L.T(StringKey.LabelRowCol, loc.Range.Start.Line + 1, loc.Range.Start.Character + 1)}");
                response.AppendLine();
            }

            return McpResultBuilder.Success().WithText(response.ToString()).Build();
        } catch (Exception ex) {
            return McpResultBuilder.Error().WithText(L.T(StringKey.LspError, ex.Message)).Build();
        }
    }

    /// <summary>
    /// 准备调用层次
    /// </summary>
    [McpTool(CodeToolNameConstants.LspPrepareCallHierarchy, "Prepare call hierarchy information", "lsp")]
    public async Task<ToolResult> LspPrepareCallHierarchyAsync(
        [McpToolParameter("File path")] string file_path,
        [McpToolParameter("Line number (1-based, consistent with editor)")] int line,
        [McpToolParameter("Character position (1-based, consistent with editor)")] int character,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(file_path)) {
            return McpResultBuilder.Error().WithText(L.T(StringKey.FilePathCannotBeEmpty)).Build();
        }

        var fileResult = await _fileOperationService.ReadFileAsync(file_path, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!fileResult.Success) {
            return McpResultBuilder.Error().WithText(L.T(StringKey.FileNotExist, file_path)).Build();
        }

        try {
            var items = await _lspService.PrepareCallHierarchyAsync(file_path, line - 1, character - 1, cancellationToken).ConfigureAwait(false);

            if (items.Count == 0) {
                return McpResultBuilder.Success().WithText(L.T(StringKey.NoCallHierarchyInfo)).Build();
            }

            var response = new System.Text.StringBuilder();
            response.AppendLine(L.T(StringKey.FoundCallHierarchyItems, items.Count));
            response.AppendLine();

            for (int i = 0; i < items.Count; i++) {
                var item = items[i];
                var fileUri = item.Uri;
                var displayPath = UriToFilePath(fileUri);
                var kindIcon = GetSymbolKindIcon(item.Kind);

                response.AppendLine($"{i + 1}. {kindIcon} {item.Name}");
                if (!string.IsNullOrEmpty(item.Detail)) {
                    response.AppendLine($"   {item.Detail}");
                }
                response.AppendLine($"   {L.T(StringKey.LabelLocation, displayPath, item.SelectionRange.Start.Line + 1)}");
                response.AppendLine();
            }

            return McpResultBuilder.Success().WithText(response.ToString()).Build();
        } catch (Exception ex) {
            return McpResultBuilder.Error().WithText(L.T(StringKey.LspError, ex.Message)).Build();
        }
    }

    /// <summary>
    /// 传入调用（谁调用了这个符号）
    /// </summary>
    [McpTool(CodeToolNameConstants.LspIncomingCalls, "Find incoming calls (who calls this symbol)", "lsp")]
    public async Task<ToolResult> LspIncomingCallsAsync(
        [McpToolParameter("File path")] string file_path,
        [McpToolParameter("Line number (1-based, consistent with editor)")] int line,
        [McpToolParameter("Character position (1-based, consistent with editor)")] int character,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(file_path)) {
            return McpResultBuilder.Error().WithText(L.T(StringKey.FilePathCannotBeEmpty)).Build();
        }

        var fileResult = await _fileOperationService.ReadFileAsync(file_path, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!fileResult.Success) {
            return McpResultBuilder.Error().WithText(L.T(StringKey.FileNotExist, file_path)).Build();
        }

        try {
            var items = await _lspService.PrepareCallHierarchyAsync(file_path, line - 1, character - 1, cancellationToken).ConfigureAwait(false);

            if (items.Count == 0) {
                return McpResultBuilder.Success().WithText(L.T(StringKey.NoCallHierarchyInfo)).Build();
            }

            var calls = await _lspService.CallHierarchyIncomingCallsAsync(items[0], cancellationToken).ConfigureAwait(false);

            if (calls.Count == 0) {
                return McpResultBuilder.Success().WithText(L.T(StringKey.NoIncomingCalls, items[0].Name)).Build();
            }

            var response = new System.Text.StringBuilder();
            response.AppendLine(L.T(StringKey.IncomingCallsOf, items[0].Name, calls.Count));
            response.AppendLine();

            for (int i = 0; i < calls.Count; i++) {
                var call = calls[i];
                var from = call.From;
                var fileUri = from.Uri;
                var displayPath = UriToFilePath(fileUri);
                var kindIcon = GetSymbolKindIcon(from.Kind);

                response.AppendLine($"{i + 1}. {kindIcon} {from.Name}");
                if (!string.IsNullOrEmpty(from.Detail)) {
                    response.AppendLine($"   {from.Detail}");
                }
                response.AppendLine($"   {L.T(StringKey.LabelFile, displayPath)}");

                if (call.FromRanges.Count > 0) {
                    foreach (var range in call.FromRanges) {
                        response.AppendLine($"   {L.T(StringKey.LabelCallSite)} {L.T(StringKey.LabelRowColShort, range.Start.Line + 1, range.Start.Character + 1)}");
                    }
                }

                response.AppendLine();
            }

            return McpResultBuilder.Success().WithText(response.ToString()).Build();
        } catch (Exception ex) {
            return McpResultBuilder.Error().WithText(L.T(StringKey.LspError, ex.Message)).Build();
        }
    }

    /// <summary>
    /// 传出调用（此符号调用了谁）
    /// </summary>
    [McpTool(CodeToolNameConstants.LspOutgoingCalls, "Find outgoing calls (symbols called by this symbol)", "lsp")]
    public async Task<ToolResult> LspOutgoingCallsAsync(
        [McpToolParameter("File path")] string file_path,
        [McpToolParameter("Line number (1-based, consistent with editor)")] int line,
        [McpToolParameter("Character position (1-based, consistent with editor)")] int character,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(file_path)) {
            return McpResultBuilder.Error().WithText(L.T(StringKey.FilePathCannotBeEmpty)).Build();
        }

        var fileResult = await _fileOperationService.ReadFileAsync(file_path, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!fileResult.Success) {
            return McpResultBuilder.Error().WithText(L.T(StringKey.FileNotExist, file_path)).Build();
        }

        try {
            var items = await _lspService.PrepareCallHierarchyAsync(file_path, line - 1, character - 1, cancellationToken).ConfigureAwait(false);

            if (items.Count == 0) {
                return McpResultBuilder.Success().WithText(L.T(StringKey.NoCallHierarchyInfo)).Build();
            }

            var calls = await _lspService.CallHierarchyOutgoingCallsAsync(items[0], cancellationToken).ConfigureAwait(false);

            if (calls.Count == 0) {
                return McpResultBuilder.Success().WithText(L.T(StringKey.NoOutgoingCalls, items[0].Name)).Build();
            }

            var response = new System.Text.StringBuilder();
            response.AppendLine(L.T(StringKey.OutgoingCallsOf, items[0].Name, calls.Count));
            response.AppendLine();

            for (int i = 0; i < calls.Count; i++) {
                var call = calls[i];
                var to = call.To;
                var fileUri = to.Uri;
                var displayPath = UriToFilePath(fileUri);
                var kindIcon = GetSymbolKindIcon(to.Kind);

                response.AppendLine($"{i + 1}. {kindIcon} {to.Name}");
                if (!string.IsNullOrEmpty(to.Detail)) {
                    response.AppendLine($"   {to.Detail}");
                }
                response.AppendLine($"   {L.T(StringKey.LabelFile, displayPath)}");

                if (call.FromRanges.Count > 0) {
                    foreach (var range in call.FromRanges) {
                        response.AppendLine($"   {L.T(StringKey.LabelCallSite)} {L.T(StringKey.LabelRowColShort, range.Start.Line + 1, range.Start.Character + 1)}");
                    }
                }

                response.AppendLine();
            }

            return McpResultBuilder.Success().WithText(response.ToString()).Build();
        } catch (Exception ex) {
            return McpResultBuilder.Error().WithText(L.T(StringKey.LspError, ex.Message)).Build();
        }
    }

    #region Private Methods

    private static string UriToFilePath(string uri) {
        var filePath = uri.StartsWith("file://") ? uri[7..] : uri;

        if (filePath.Length > 2 && filePath[0] == '/' && char.IsLetter(filePath[1]) && filePath[2] == ':') {
            filePath = filePath[1..];
        }

        try {
            filePath = Uri.UnescapeDataString(filePath);
        } catch (Exception ex) {
            System.Diagnostics.Trace.WriteLine($"Failed to unescape URI data string: {ex.Message}");
        }

        return filePath;
    }

    private static async Task<HashSet<string>> GetGitIgnoredPathsAsync(List<string> paths, string cwd) {
        var ignored = new HashSet<string>();
        if (paths.Count == 0) return ignored;

        const int batchSize = 50;
        for (var i = 0; i < paths.Count; i += batchSize) {
            var batch = paths.Skip(i).Take(batchSize).ToList();
            var args = string.Join(" ", new[] { "check-ignore" }.Concat(batch.Select(p => $"\"{p}\"")));

            try {
                using var process = new System.Diagnostics.Process {
                    StartInfo = new System.Diagnostics.ProcessStartInfo {
                        FileName = "git",
                        Arguments = args,
                        WorkingDirectory = cwd,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();
                await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
                await process.WaitForExitAsync().ConfigureAwait(false);

                var stdout = await stdoutTask.ConfigureAwait(false);

                if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(stdout)) {
                    foreach (var line in stdout.Split('\n')) {
                        var trimmed = line.Trim();
                        if (!string.IsNullOrEmpty(trimmed)) {
                            ignored.Add(trimmed);
                        }
                    }
                }
            } catch (Exception ex) {
                System.Diagnostics.Trace.WriteLine($"Git check-ignore failed: {ex.Message}");
            }
        }

        return ignored;
    }

    private static List<LspLocation> FilterGitIgnoredLocations(List<LspLocation> locations, HashSet<string> ignoredPaths) {
        if (ignoredPaths.Count == 0) return locations;

        return locations.Where(loc => {
            var filePath = UriToFilePath(loc.Uri);
            return !ignoredPaths.Contains(filePath);
        }).ToList();
    }

    private static List<LspSymbolInformation> FilterGitIgnoredSymbols(List<LspSymbolInformation> symbols, HashSet<string> ignoredPaths) {
        if (ignoredPaths.Count == 0) return symbols;

        return symbols.Where(sym => {
            var filePath = UriToFilePath(sym.Location.Uri);
            return !ignoredPaths.Contains(filePath);
        }).ToList();
    }

    private async Task<List<LspLocation>> FilterLocationsGitIgnoredAsync(List<LspLocation> locations) {
        if (locations.Count == 0) return locations;

        var cwd = _fileOperationService.GetCurrentDirectory();
        var uniquePaths = locations
            .Select(l => UriToFilePath(l.Uri))
            .Where(p => !string.IsNullOrEmpty(p))
            .Distinct()
            .ToList();

        if (uniquePaths.Count == 0) return locations;

        var ignored = await GetGitIgnoredPathsAsync(uniquePaths, cwd).ConfigureAwait(false);
        return FilterGitIgnoredLocations(locations, ignored);
    }

    private async Task<List<LspSymbolInformation>> FilterSymbolsGitIgnoredAsync(List<LspSymbolInformation> symbols) {
        if (symbols.Count == 0) return symbols;

        var cwd = _fileOperationService.GetCurrentDirectory();
        var uniquePaths = symbols
            .Select(s => UriToFilePath(s.Location.Uri))
            .Where(p => !string.IsNullOrEmpty(p))
            .Distinct()
            .ToList();

        if (uniquePaths.Count == 0) return symbols;

        var ignored = await GetGitIgnoredPathsAsync(uniquePaths, cwd).ConfigureAwait(false);
        return FilterGitIgnoredSymbols(symbols, ignored);
    }

    private static string FormatHoverContents(JsonElement? contents) {
        if (contents is null) return "";
        var json = contents.Value;
        return json.ValueKind switch {
            JsonValueKind.String => json.GetString() ?? "",
            JsonValueKind.Array => string.Join("\n", json.EnumerateArray().Select(e => FormatHoverContents(e))),
            JsonValueKind.Object => json.TryGetProperty("value", out var value) ? value.GetString() ?? "" : json.GetRawText(),
            _ => json.GetRawText()
        };
    }

    private static string FormatHoverContents(JsonElement json) {
        return json.ValueKind switch {
            JsonValueKind.String => json.GetString() ?? "",
            JsonValueKind.Array => string.Join("\n", json.EnumerateArray().Select(e => FormatHoverContents(e))),
            JsonValueKind.Object => json.TryGetProperty("value", out var value) ? value.GetString() ?? "" : json.GetRawText(),
            _ => json.GetRawText()
        };
    }

    private static string FormatDocumentation(JsonElement? documentation) {
        if (documentation is null) return "";
        var json = documentation.Value;
        return json.TryGetProperty("value", out var value) ? value.GetString() ?? "" : json.GetString() ?? "";
    }

    private static void FormatSymbol(System.Text.StringBuilder sb, LspDocumentSymbol symbol, int indent) {
        var prefix = new string(' ', indent * 2);
        var kindIcon = GetSymbolKindIcon(symbol.Kind);

        sb.AppendLine($"{prefix}{kindIcon} {symbol.Name}");

        if (!string.IsNullOrEmpty(symbol.Detail)) {
            sb.AppendLine($"{prefix}   {symbol.Detail}");
        }

        if (symbol.Children != null) {
            foreach (var child in symbol.Children) {
                FormatSymbol(sb, child, indent + 1);
            }
        }
    }

    private static string GetCompletionKindIcon(int? kind) {
        return kind switch {
            1 => ObjectSymbol.File.ToValue(),  // Text
            2 => ObjectSymbol.Directory.ToValue(),  // Method
            3 => ObjectSymbol.Directory.ToValue(),  // Function
            4 => ObjectSymbol.Directory.ToValue(),  // Constructor
            5 => ObjectSymbol.DiamondFilled.ToValue(),  // Field
            6 => ObjectSymbol.DiamondFilled.ToValue(),  // Variable
            7 => ObjectSymbol.DiamondOpen.ToValue(),  // Class
            8 => ObjectSymbol.ArrowRight.ToValue(),  // Interface
            9 => ObjectSymbol.DiamondFilled.ToValue(),  // Module
            10 => StatusSymbol.Stop.ToValue(), // Property
            11 => ObjectSymbol.File.ToValue(), // Unit
            12 => ObjectSymbol.DiamondOpen.ToValue(), // Value
            13 => ObjectSymbol.List.ToValue(), // Enum
            14 => ObjectSymbol.DiamondFilled.ToValue(), // Keyword
            15 => ObjectSymbol.Pencil.ToValue(), // Snippet
            16 => ObjectSymbol.Color.ToValue(), // Color
            17 => ObjectSymbol.Directory.ToValue(), // File
            18 => ObjectSymbol.Directory.ToValue(), // Reference
            19 => ObjectSymbol.Directory.ToValue(), // Folder
            20 => ObjectSymbol.DiamondOpen.ToValue(), // EnumMember
            21 => ObjectSymbol.DiamondFilled.ToValue(), // Constant
            22 => ObjectSymbol.Struct.ToValue(), // Struct
            23 => ObjectSymbol.Lightning.ToValue(),  // Event
            24 => ObjectSymbol.Directory.ToValue(), // Operator
            25 => ObjectSymbol.DiamondOpen.ToValue(), // TypeParameter
            _ => ObjectSymbol.File.ToValue()
        };
    }

    private static string GetSymbolKindIcon(int kind) {
        return kind switch {
            1 => ObjectSymbol.Directory.ToValue(),  // File
            2 => ObjectSymbol.DiamondFilled.ToValue(),  // Module
            3 => ObjectSymbol.DiamondFilled.ToValue(),  // Namespace
            4 => ObjectSymbol.DiamondFilled.ToValue(),  // Package
            5 => ObjectSymbol.Struct.ToValue(),  // Class
            6 => ObjectSymbol.Directory.ToValue(),  // Method
            7 => ObjectSymbol.DiamondFilled.ToValue(),  // Property
            8 => ObjectSymbol.DiamondFilled.ToValue(),  // Field
            9 => ObjectSymbol.Directory.ToValue(),  // Constructor
            10 => ObjectSymbol.List.ToValue(), // Enum
            11 => ObjectSymbol.ArrowRight.ToValue(), // Interface
            12 => ObjectSymbol.Directory.ToValue(), // Function
            13 => ObjectSymbol.DiamondFilled.ToValue(), // Variable
            14 => ObjectSymbol.DiamondOpen.ToValue(), // Constant
            15 => ObjectSymbol.Pencil.ToValue(), // String
            16 => ObjectSymbol.DiamondOpen.ToValue(), // Number
            17 => ObjectSymbol.DiamondFilled.ToValue(), // Boolean
            18 => ObjectSymbol.File.ToValue(), // Array
            19 => ObjectSymbol.File.ToValue(), // Object
            20 => ObjectSymbol.DiamondFilled.ToValue(), // Key
            21 => ObjectSymbol.DiamondOpen.ToValue(), // Null
            22 => ObjectSymbol.DiamondOpen.ToValue(), // EnumMember
            23 => ObjectSymbol.Struct.ToValue(), // Struct
            24 => ObjectSymbol.Lightning.ToValue(),  // Event
            25 => ObjectSymbol.Directory.ToValue(), // Operator
            26 => ObjectSymbol.DiamondOpen.ToValue(), // TypeParameter
            _ => ObjectSymbol.File.ToValue()
        };
    }

    #endregion
}
