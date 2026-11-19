// code 组下沉 handler — 9 个 C# LLM 分析/生成 + 3 个 recall 文件读取 + 10 个 lsp_*。
// 全部只用 BCL：LLM 用 HttpClient 直调（CodeLlm，不引 SDK）；recall 读 ~/.jcc/notebook/default/*.json；
// lsp_* 由 exe 自己起语言服务器进程（LspConnection，JSON-RPC over stdio），不再依赖宿主常驻实例。






namespace JoinCode.CliNode.Code;

internal static partial class CodeCommands {
    private static readonly FrozenDictionary<string, CliNodeHandler> Handlers =
        new Dictionary<string, CliNodeHandler>(StringComparer.Ordinal) {
            // C# LLM 分析/生成（HttpClient 直调）
            ["analyze_csharp_code"] = AnalyzeCSharpAsync,
            ["find_bugs"] = FindBugsAsync,
            ["optimize_code"] = OptimizeCodeAsync,
            ["security_audit"] = SecurityAuditAsync,
            ["generate_csharp_code"] = GenerateCSharpAsync,
            ["generate_unit_test"] = GenerateUnitTestAsync,
            ["generate_api_controller"] = GenerateApiControllerAsync,
            ["mcp_ai_workflow_workflow_generate_code"] = WorkflowGenerateCodeAsync,
            ["mcp_ai_workflow_workflow_analyze_code"] = WorkflowAnalyzeCodeAsync,
            // recall（BCL 读文件，单一真相源）
            ["recall_list"] = RecallListAsync,
            ["recall_read"] = RecallReadAsync,
            ["recall_search"] = RecallSearchAsync,
            // LSP（exe 自持语言服务器进程）
            ["lsp_goto_definition"] = LspGotoDefinitionAsync,
            ["lsp_goto_implementation"] = LspGotoImplementationAsync,
            ["lsp_find_references"] = LspFindReferencesAsync,
            ["lsp_hover"] = LspHoverAsync,
            ["lsp_completion"] = LspCompletionAsync,
            ["lsp_document_symbols"] = LspDocumentSymbolsAsync,
            ["lsp_workspace_symbol"] = LspWorkspaceSymbolAsync,
            ["lsp_prepare_call_hierarchy"] = LspPrepareCallHierarchyAsync,
            ["lsp_incoming_calls"] = LspIncomingCallsAsync,
            ["lsp_outgoing_calls"] = LspOutgoingCallsAsync
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>合并路由：先查 notebook（10），再查本表（12 + 10 个 lsp）。</summary>
    public static CliNodeHandler? Find(string toolId) => NotebookCommands.Find(toolId) ?? (Handlers.TryGetValue(toolId, out var h) ? h : null);

    public static IEnumerable<string> LocalToolIds => NotebookCommands.LocalToolIds.Concat(Handlers.Keys).OrderBy(id => id, StringComparer.Ordinal);

    // ==================== C# LLM 分析/生成 ====================

    private static Task<int> AnalyzeCSharpAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var code = Required(o, "code", "analyze_csharp_code");
        var focus = Get(o, "focus", "all");
        var prompt = $"""
            你是一位资深的 C# 代码审查专家。请分析以下代码并提供详细的改进建议。

            ## 待分析代码
            ```csharp
            {code}
            ```

            ## 分析重点: {focus}

            请提供详细的代码分析报告，包括：
            1. 代码质量评估
            2. 发现的问题和改进建议
            3. 最佳实践符合度
            4. 具体的重构建议（如有）
            """;
        return AskAndPrintAsync(prompt, ct);
    }

    private static Task<int> FindBugsAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var code = Required(o, "code", "find_bugs");
        var severity = Get(o, "severity", "all");
        var prompt = $"""
            你是一位专业的 Bug 猎人。请仔细检查以下 C# 代码中的潜在缺陷。

            ## 待检查代码
            ```csharp
            {code}
            ```

            ## 严重程度筛选: {severity}

            请列出发现的每个 Bug，包括严重程度、位置、原因和修复建议。
            """;
        return AskAndPrintAsync(prompt, ct);
    }

    private static Task<int> OptimizeCodeAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var code = Required(o, "code", "optimize_code");
        var target = Get(o, "target", "all");
        var prompt = $"""
            你是一位代码优化专家。请优化以下 C# 代码。

            ## 待优化代码
            ```csharp
            {code}
            ```

            ## 优化目标: {target}

            请给出优化后的代码和说明（性能/内存/可读性）。
            """;
        return AskAndPrintAsync(prompt, ct);
    }

    private static Task<int> SecurityAuditAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var code = Required(o, "code", "security_audit");
        var auditType = Get(o, "audit_type", "general");
        var prompt = $"""
            你是一位安全审计专家。请对以下 C# 代码进行安全审计。

            ## 待审计代码
            ```csharp
            {code}
            ```

            ## 审计类型: {auditType}

            请列出安全风险（注入/越权/敏感信息泄露等）、等级与修复建议。
            """;
        return AskAndPrintAsync(prompt, ct);
    }

    private static Task<int> GenerateCSharpAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var desc = Required(o, "description", "generate_csharp_code");
        var context = Get(o, "context", "");
        var framework = Get(o, "framework_version", "");
        var prompt = $"""
            你是一位专业的 C# 开发专家。请根据描述生成 C# 代码。

            需求: {desc}
            {(!string.IsNullOrEmpty(context) ? "上下文: " + context + "\n" : "")}
            {(!string.IsNullOrEmpty(framework) ? "框架版本: " + framework + "\n" : "")}
            只输出代码，附必要说明。
            """;
        return AskAndPrintAsync(prompt, ct);
    }

    private static Task<int> GenerateUnitTestAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var code = Required(o, "code", "generate_unit_test");
        var framework = Get(o, "test_framework", "xunit");
        var count = GetInt(o, "test_count", 5);
        var prompt = $"""
            你是一位单元测试专家。请为以下 C# 代码生成单元测试（{framework}，约 {count} 个用例）。

            ```csharp
            {code}
            ```

            只输出测试代码。
            """;
        return AskAndPrintAsync(prompt, ct);
    }

    private static Task<int> GenerateApiControllerAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var desc = Required(o, "description", "generate_api_controller");
        var model = Get(o, "model_definition", "");
        var crud = GetBool(o, "include_crud", true);
        var auth = GetBool(o, "include_auth", false);
        var prompt = $"""
            你是一位 ASP.NET Core 开发专家。请生成 API Controller。

            需求: {desc}
            {(!string.IsNullOrEmpty(model) ? "模型定义: " + model + "\n" : "")}
            包含 CRUD: {crud}; 包含鉴权: {auth}

            只输出代码。
            """;
        return AskAndPrintAsync(prompt, ct);
    }

    private static Task<int> WorkflowGenerateCodeAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var requirement = Required(o, "requirement", "mcp_ai_workflow_workflow_generate_code");
        var prompt = $"你是一位专业的 C# 开发专家。请根据以下需求生成完整代码:\n{requirement}\n只输出代码与简要说明。";
        return AskAndPrintAsync(prompt, ct);
    }

    private static Task<int> WorkflowAnalyzeCodeAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var code = Required(o, "code", "mcp_ai_workflow_workflow_analyze_code");
        var analysisType = Get(o, "analysisType", "general");
        var prompt = $"你是一位资深代码分析专家。请按 {analysisType} 类型分析以下代码:\n```csharp\n{code}\n```";
        return AskAndPrintAsync(prompt, ct);
    }

    private static async Task<int> AskAndPrintAsync(string prompt, CancellationToken ct) {
        try {
            var reply = await CodeLlm.AskTextAsync("你是专业的 C# 开发助手。", prompt, ct).ConfigureAwait(false);
            Console.Out.Write(reply);
            return 0;
        } catch (ArgumentException ex) {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
    }

    // ==================== recall（纯文件读取） ====================

    private static string RecallDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".jcc", "notebook", "default");

    private sealed class FoldEntry {
        public string? FoldId { get; set; }
        public DateTimeOffset Timestamp { get; set; }
        public int MessageCount { get; set; }
        public string? Summary { get; set; }
    }

    private static Task<int> RecallListAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        if (!Directory.Exists(RecallDir)) { Console.Out.WriteLine("No folded archives found. Archives are created automatically when conversation history is folded or compacted."); return Task.FromResult(0); }
        var entries = Directory.GetFiles(RecallDir, "*.json").Select(f => {
            try { return JsonSerializer.Deserialize(File.ReadAllText(f), CodeJsonContext.Default.FoldEntry); }
            catch (JsonException) { return null; }
        }).Where(e => e is not null).Select(e => e).ToList();
        if (entries.Count == 0) { Console.Out.WriteLine("No folded archives found. Archives are created automatically when conversation history is folded or compacted."); return Task.FromResult(0); }
        var sb = new StringBuilder();
        sb.AppendLine($"Found {entries.Count} folded archive(s):");
        sb.AppendLine();
        foreach (var e in entries) {
            sb.AppendLine($"  [{e.FoldId}]");
            sb.AppendLine($"    Time: {e.Timestamp:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"    Messages: {e.MessageCount}");
            sb.AppendLine($"    Summary: {Truncate(e.Summary ?? "", 120)}");
            sb.AppendLine();
        }
        sb.AppendLine("Use recall_read with a fold_id to read the full original content of any archive.");
        Console.Out.Write(sb.ToString());
        return Task.FromResult(0);
    }

    private static Task<int> RecallReadAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var foldId = Required(o, "fold_id", "recall_read");
        var path = Path.Combine(RecallDir, foldId + ".json");
        if (!File.Exists(path)) { Console.Out.WriteLine($"Archive '{foldId}' not found. Use recall_list to see available archives."); return Task.FromResult(0); }
        Console.Out.Write(File.ReadAllText(path));
        return Task.FromResult(0);
    }

    private static Task<int> RecallSearchAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var keyword = Required(o, "keyword", "recall_search");
        if (!Directory.Exists(RecallDir)) { Console.Out.WriteLine($"No archives found matching '{keyword}'."); return Task.FromResult(0); }
        var matches = Directory.GetFiles(RecallDir, "*.json")
            .Select(f => (path: f, text: File.ReadAllText(f)))
            .Where(x => x.text.Contains(keyword, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count == 0) { Console.Out.WriteLine($"No archives found matching '{keyword}'."); return Task.FromResult(0); }
        var sb = new StringBuilder();
        sb.AppendLine($"Found {matches.Count} archive(s) matching '{keyword}':");
        sb.AppendLine();
        foreach (var (path, text) in matches) {
            var e = JsonSerializer.Deserialize(text, CodeJsonContext.Default.FoldEntry);
            sb.AppendLine($"  [{Path.GetFileNameWithoutExtension(path)}]");
            sb.AppendLine($"    Time: {e?.Timestamp:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"    Messages: {e?.MessageCount ?? 0}");
            sb.AppendLine($"    Summary: {Truncate(e?.Summary ?? "", 120)}");
            sb.AppendLine();
        }
        sb.AppendLine("Use recall_read with a fold_id to read the full original content.");
        Console.Out.Write(sb.ToString());
        return Task.FromResult(0);
    }

    private static string Required(IReadOnlyDictionary<string, string> o, string key, string tool) =>
        o.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : throw new ArgumentException($"{tool} 缺少必填参数 --{key}。正确写法: jcc tool call code {tool} --{key} <值>");
    private static string Get(IReadOnlyDictionary<string, string> o, string key, string fallback) =>
        o.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;
    private static string? Nullable(IReadOnlyDictionary<string, string> o, string key) =>
        o.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;
    private static int GetInt(IReadOnlyDictionary<string, string> o, string key, int fallback) => int.TryParse(Get(o, key, ""), out var v) ? v : fallback;
    private static bool GetBool(IReadOnlyDictionary<string, string> o, string key, bool fallback) => o.TryGetValue(key, out var v) && bool.TryParse(v, out var b) ? b : fallback;
    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "...";

    [JsonSerializable(typeof(FoldEntry))]
    [JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
    private sealed partial class CodeJsonContext : JsonSerializerContext;
}
