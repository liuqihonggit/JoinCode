// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
﻿namespace Integration.Tests.McpTools;

/// <summary>
/// 工具测试上下文 — BuildAndRegisterAllToolsAsync 的返回值，替代元组更清晰
/// </summary>
public sealed record ToolTestContext {
    /// <summary>工具注册表（直连，绕过权限管道）</summary>
    public required Tools.LocalToolRegistry Registry { get; init; }
    /// <summary>工具执行网关（走 13 层权限管道）</summary>
    public required IToolExecutionGateway Gateway { get; init; }
    /// <summary>所有已注册工具信息</summary>
    public required IReadOnlyList<ToolInfo> Tools { get; init; }
    /// <summary>临时目录（测试隔离用）</summary>
    public required string TempDir { get; init; }
    /// <summary>工作目录（IFileSystem.GetCurrentDirectory）</summary>
    public required string WorkingDir { get; init; }
}

/// <summary>
/// MCP 工具冒烟测试 — 验证每个已注册工具能被调用且返回有效 ToolResult
/// 不验证工具的业务逻辑，只验证不崩溃
/// 标记 Integration 因遍历全部工具含桌面鼠标/键盘操作（Win32 SendInput 副作用）
/// </summary>
[Trait("Category", "Integration")]
public sealed class McpToolSmokeTests {
    private static async Task<ToolTestContext> BuildAndRegisterAllToolsAsync() {
        var tempDir = Path.Combine(Path.GetTempPath(), $"jcc-test-{Guid.NewGuid():N}");
        Environment.SetEnvironmentVariable(JccEnvVarEnumConstants.AppDataFolder, tempDir);
        Environment.SetEnvironmentVariable("JCC_FILE_SYSTEM_MODE", "InMemory");

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        var config = new WorkflowConfig();
        services.AddWorkflowServices(config);
        services.AddTestPipelines();

        var provider = services.BuildServiceProvider();

        var mcpService = provider.GetRequiredService<IMcpService>();
        await mcpService.InitializeAsync(provider).ConfigureAwait(true);

        var toolRegistry = provider.GetRequiredService<IMcpToolRegistry>();
        var allTools = await toolRegistry.GetAllToolsAsync().ConfigureAwait(true);

        var registry = provider.GetRequiredService<Tools.LocalToolRegistry>();
        var gateway = provider.GetRequiredService<IToolExecutionGateway>();
        var toolInfos = await registry.GetAllToolInfosAsync().ConfigureAwait(true);
        var fs = provider.GetRequiredService<IFileSystem>();
        var workingDir = fs.GetCurrentDirectory();
        return new ToolTestContext {
            Registry = registry,
            Gateway = gateway,
            Tools = toolInfos,
            TempDir = tempDir,
            WorkingDir = workingDir,
        };
    }

    /// <summary>
    /// 为工具构造最小参数集 — 对必需参数提供最小有效值
    /// </summary>
    private static Dictionary<string, JsonElement> BuildMinimalArguments(ToolInfo tool) {
        var args = new Dictionary<string, JsonElement>();

        if (tool.InputSchema?.Required == null) return args;

        foreach (var reqParam in tool.InputSchema.Required) {
            if (!tool.InputSchema.Properties.TryGetValue(reqParam, out var prop)) continue;

            var value = prop.Type switch {
                "string" => reqParam switch {
                    "file_path" or "path" => JsonDocument.Parse("\"test.txt\"").RootElement.Clone(),
                    "directory_path" => JsonDocument.Parse("\".\"").RootElement.Clone(),
                    "command" => JsonDocument.Parse("\"echo hello\"").RootElement.Clone(),
                    "pattern" => JsonDocument.Parse("\"*.txt\"").RootElement.Clone(),
                    "query" => JsonDocument.Parse("\"test\"").RootElement.Clone(),
                    "url" => JsonDocument.Parse("\"http://localhost\"").RootElement.Clone(),
                    "prompt" => JsonDocument.Parse("\"test\"").RootElement.Clone(),
                    "code" => JsonDocument.Parse("\"var x = 1;\"").RootElement.Clone(),
                    "expression" => JsonDocument.Parse("\"1+1\"").RootElement.Clone(),
                    "schema_name" => JsonDocument.Parse("\"test_schema\"").RootElement.Clone(),
                    "schema_json" => JsonDocument.Parse("\"{}\"").RootElement.Clone(),
                    "action" => JsonDocument.Parse("\"list\"").RootElement.Clone(),
                    "target" => JsonDocument.Parse("\"test\"").RootElement.Clone(),
                    "confirm" => JsonDocument.Parse("\"yes\"").RootElement.Clone(),
                    "agent_pattern" => JsonDocument.Parse("\"*\"").RootElement.Clone(),
                    "agent_name" => JsonDocument.Parse("\"test\"").RootElement.Clone(),
                    "cassette_name" => JsonDocument.Parse("\"test\"").RootElement.Clone(),
                    "client_id" => JsonDocument.Parse("\"test\"").RootElement.Clone(),
                    "tool_name" => JsonDocument.Parse("\"test\"").RootElement.Clone(),
                    "resource_uri" => JsonDocument.Parse("\"test://resource\"").RootElement.Clone(),
                    "auth_id" => JsonDocument.Parse("\"test\"").RootElement.Clone(),
                    "api_key" => JsonDocument.Parse("\"test-key\"").RootElement.Clone(),
                    "token" => JsonDocument.Parse("\"test-token\"").RootElement.Clone(),
                    "username" => JsonDocument.Parse("\"test\"").RootElement.Clone(),
                    "password" => JsonDocument.Parse("\"test\"").RootElement.Clone(),
                    "refresh_token" => JsonDocument.Parse("\"test\"").RootElement.Clone(),
                    "client_secret" => JsonDocument.Parse("\"test\"").RootElement.Clone(),
                    "auth_url" => JsonDocument.Parse("\"http://localhost/auth\"").RootElement.Clone(),
                    "token_url" => JsonDocument.Parse("\"http://localhost/token\"").RootElement.Clone(),
                    _ => JsonDocument.Parse("\"test\"").RootElement.Clone(),
                },
                "integer" => JsonDocument.Parse("1").RootElement.Clone(),
                "boolean" => JsonDocument.Parse("false").RootElement.Clone(),
                "array" => JsonDocument.Parse("[]").RootElement.Clone(),
                "object" => JsonDocument.Parse("{}").RootElement.Clone(),
                _ => JsonDocument.Parse("\"test\"").RootElement.Clone()
            };

            args[reqParam] = value;
        }

        return args;
    }

    /// <summary>
    /// 有真实 git 副作用的工具 — 冒烟测试跳过，避免污染测试环境（创建/删除真实 git worktree）
    /// </summary>
    private static readonly FrozenSet<string> ToolsWithGitSideEffects = FrozenSet.Create(
        StringComparer.Ordinal,
        [
            "worktree_create",
            "worktree_remove",
            "worktree_cleanup",
            "worktree_merge",
            "enter_worktree",
            "exit_worktree",
            "git_status",
            "git_add",
            "git_commit",
            "git_push",
            "git_pull",
            "git_log",
            "git_diff",
            "git_branch",
            "git_clone",
            "git_reset",
            "git_clean",
        ]
    );

    /// <summary>
    /// 有真实网络副作用的工具 — 冒烟测试跳过，避免连接外部服务器导致超时（mcp_connect 连真实 MCP 服务器）
    /// </summary>
    private static readonly FrozenSet<string> ToolsWithNetworkSideEffects = FrozenSet.Create(
        StringComparer.Ordinal,
        [
            "mcp_connect",
        ]
    );

    /// <summary>
    /// 判断工具是否有真实副作用（桌面 Win32 SendInput / git worktree / 网络连接 / GitHub 写入 / git 操作），冒烟测试应跳过。
    /// <para>gh_* 全跳过：GitHub 工具有写入副作用（create issue/PR/comment、cancel/rerun run、fork repo、close/merge PR 等），
    /// 巡检用最小参数真实调用会向用户仓库写入数据。</para>
    /// <para>git_* 全跳过：git 工具有本地仓库副作用（add/commit/push/branch/clone/reset/clean 等）。</para>
    /// </summary>
    private static bool HasRealSideEffects(ToolInfo tool) =>
        string.Equals(tool.Category, "desktop", StringComparison.OrdinalIgnoreCase)
        || ToolsWithGitSideEffects.Contains(tool.Name)
        || ToolsWithNetworkSideEffects.Contains(tool.Name)
        || tool.Name.StartsWith("gh_", StringComparison.Ordinal)
        || tool.Name.StartsWith("git_", StringComparison.Ordinal);

    [Fact]
    public async Task All_Registered_Tools_Can_Be_Called_Without_Crash() {
        var ctx = await BuildAndRegisterAllToolsAsync().ConfigureAwait(true);
        var gateway = ctx.Gateway;
        var allTools = ctx.Tools;

        allTools.Should().NotBeEmpty("至少应注册一个工具");

        var results = new List<(string ToolName, string Status, string? ErrorMessage)>();
        var crashed = new List<(string ToolName, Exception Ex)>();
        var deadlocked = new List<(string ToolName, Exception Ex)>();
        var timedOut = new List<(string ToolName, Exception Ex)>();
        var skipped = new List<string>();

        foreach (var tool in allTools.OrderBy(t => t.Name)) {
            if (HasRealSideEffects(tool)) {
                skipped.Add(tool.Name);
                results.Add((tool.Name, "SKIPPED", "有真实副作用"));
                continue;
            }

            try {
                var args = BuildMinimalArguments(tool);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var result = await gateway.ExecuteAsync(tool.Name, args, cts.Token).ConfigureAwait(true);

                result.Should().NotBeNull($"工具 {tool.Name} 返回 null");
                result.Content.Should().NotBeNull($"工具 {tool.Name} 返回 null Content");

                var status = result.IsError ? "ERROR" : "OK";
                results.Add((tool.Name, status, result.IsError ? result.Content.FirstOrDefault()?.Text : null));
            } catch (ActorAskDeadlockException ex) {
                deadlocked.Add((tool.Name, ex));
                results.Add((tool.Name, "DEADLOCK", $"ActorAskDeadlock: {ex.Message}"));
            } catch (OperationCanceledException ex) {
                timedOut.Add((tool.Name, ex));
                results.Add((tool.Name, "TIMEOUT", $"30s 超时: {ex.Message}"));
            } catch (TimeoutException ex) {
                timedOut.Add((tool.Name, ex));
                results.Add((tool.Name, "TIMEOUT", $"TimeoutException: {ex.Message}"));
            } catch (Exception ex) {
                crashed.Add((tool.Name, ex));
                results.Add((tool.Name, "CRASH", $"{ex.GetType().Name}: {ex.Message}"));
            }
        }

        var report = new StringBuilder();
        report.AppendLine("# MCP 工具冒烟测试报告（走完整 16 层管道 + 30s 超时守卫）");
        report.AppendLine($"时间: {DateTimeOffset.UtcNow:O}");
        report.AppendLine($"工具总数: {allTools.Count}");
        report.AppendLine($"成功: {results.Count(r => r.Status == "OK")}");
        report.AppendLine($"错误(非崩溃): {results.Count(r => r.Status == "ERROR")}");
        report.AppendLine($"崩溃: {crashed.Count}");
        report.AppendLine($"死锁(ActorAskDeadlock): {deadlocked.Count}");
        report.AppendLine($"超时(30s): {timedOut.Count}");
        report.AppendLine($"跳过(有真实副作用): {skipped.Count}");
        report.AppendLine();

        report.AppendLine("## 详细结果（助记符验收：[OK]成功 [ERR]错误 [CRASH]崩溃 [LOCK]死锁 [TMO]超时 [SKIP]跳过）");
        foreach (var (toolName, status, errorMessage) in results.OrderBy(r => r.ToolName)) {
            var mnemonic = status switch {
                "OK" => "[OK]",
                "ERROR" => "[ERR]",
                "CRASH" => "[CRASH]",
                "DEADLOCK" => "[LOCK]",
                "TIMEOUT" => "[TMO]",
                "SKIPPED" => "[SKIP]",
                _ => $"[{status}]"
            };
            var truncatedError = errorMessage switch {
                null => "",
                _ when errorMessage.Length > 100 => errorMessage[..100] + "...",
                _ => errorMessage
            };
            report.AppendLine($"{mnemonic} {toolName} {truncatedError}");
        }

        Console.WriteLine(report.ToString());

        // 强制失败：崩溃/死锁/超时必须为 0（工具可以返回错误，但不能崩溃/死锁/超时）
        crashed.Should().BeEmpty($"以下工具崩溃: {string.Join(", ", crashed.Select(c => c.ToolName))}");
        deadlocked.Should().BeEmpty($"以下工具死锁(ActorAskDeadlockException): {string.Join(", ", deadlocked.Select(c => c.ToolName))}");
        timedOut.Should().BeEmpty($"以下工具 30s 超时: {string.Join(", ", timedOut.Select(c => c.ToolName))}");
    }

    [Fact]
    public async Task Sleep_Tool_Returns_Valid_Result() {
        var ctx = await BuildAndRegisterAllToolsAsync().ConfigureAwait(true);

        var args = new Dictionary<string, JsonElement> {
            ["duration_seconds"] = JsonDocument.Parse("1").RootElement.Clone()
        };

        var result = await ctx.Registry.ExecuteToolAsync("sleep", args, CancellationToken.None).ConfigureAwait(true);

        result.Should().NotBeNull();
        result.Content.Should().NotBeNull();
        // Sleep 工具可能因测试环境限制返回错误，但不应该崩溃
    }

    [Fact]
    public async Task Mcp_Auth_Status_Tool_Returns_Valid_Result() {
        var ctx = await BuildAndRegisterAllToolsAsync().ConfigureAwait(true);

        var result = await ctx.Registry.ExecuteToolAsync("mcp_auth_status", new Dictionary<string, JsonElement>(), CancellationToken.None).ConfigureAwait(true);

        result.Should().NotBeNull();
        result.Content.Should().NotBeNull();
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public async Task Unknown_Tool_Returns_Error_Or_Throws() {
        var ctx = await BuildAndRegisterAllToolsAsync().ConfigureAwait(true);

        // 调用不存在的工具 — 注册表可能抛出异常或返回错误结果，不能静默成功
        Exception? caughtEx = null;
        ToolResult? result = null;
        try {
            result = await ctx.Registry.ExecuteToolAsync("nonexistent_tool", new Dictionary<string, JsonElement>(), CancellationToken.None).ConfigureAwait(true);
        } catch (Exception ex) {
            // 抛出异常也是可接受的行为 — 记录以供断言
            caughtEx = ex;
        }

        if (caughtEx is not null) {
            // 抛出异常 — 验证通过
            return;
        }

        result.Should().NotBeNull();
        result!.IsError.Should().BeTrue("不存在的工具应该返回错误结果");
    }

    /// <summary>
    /// write→read 行为验证 — 写入文件后读回，断言内容一致。真实验证写入/读取行为，不只"不崩溃"。
    /// </summary>
    [Fact]
    public async Task Write_Read_Behavior_Verification() {
        var ctx = await BuildAndRegisterAllToolsAsync().ConfigureAwait(true);
        var fileName = $"patrol-behavior-test-{Guid.NewGuid():N}.txt";
        var content = "hello patrol behavior verification";
        var fileNameJson = System.Text.Json.JsonSerializer.Serialize(fileName);
        var contentJson = System.Text.Json.JsonSerializer.Serialize(content);

        // write 写入文件(相对路径,沙箱放行)
        var writeArgs = new Dictionary<string, JsonElement> {
            ["file_path"] = JsonDocument.Parse(fileNameJson).RootElement.Clone(),
            ["content"] = JsonDocument.Parse(contentJson).RootElement.Clone(),
        };
        var writeResult = await ctx.Gateway.ExecuteAsync("write", writeArgs, CancellationToken.None).ConfigureAwait(true);
        writeResult.IsError.Should().BeFalse($"write 应成功: {writeResult.Content.FirstOrDefault()?.Text}");

        // read 读回文件
        var readArgs = new Dictionary<string, JsonElement> {
            ["file_path"] = JsonDocument.Parse(fileNameJson).RootElement.Clone(),
        };
        var readResult = await ctx.Gateway.ExecuteAsync("read", readArgs, CancellationToken.None).ConfigureAwait(true);
        readResult.IsError.Should().BeFalse($"read 应成功: {readResult.Content.FirstOrDefault()?.Text}");

        // 断言读回内容含写入内容 — 真实行为验证
        var readText = readResult.Content.FirstOrDefault()?.Text ?? "";
        readText.Should().Contain(content, "read 应返回写入的内容（行为验证：不只不崩溃，验证内容一致）");
    }

    /// <summary>
    /// write→list 行为验证 — 写入文件后列目录，断言列表含写入文件名。
    /// </summary>
    [Fact]
    public async Task Write_List_Behavior_Verification() {
        var ctx = await BuildAndRegisterAllToolsAsync().ConfigureAwait(true);
        var fileName = $"patrol-list-test-{Guid.NewGuid():N}.txt";

        // write 写入文件(相对路径)
        var writeArgs = new Dictionary<string, JsonElement> {
            ["file_path"] = JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(fileName)).RootElement.Clone(),
            ["content"] = JsonDocument.Parse("\"patrol list test\"").RootElement.Clone(),
        };
        var writeResult = await ctx.Gateway.ExecuteAsync("write", writeArgs, CancellationToken.None).ConfigureAwait(true);
        writeResult.IsError.Should().BeFalse($"write 应成功: {writeResult.Content.FirstOrDefault()?.Text}");

        // directory_list 列当前目录(用 IFileSystem.GetCurrentDirectory,路径一致)
        var listArgs = new Dictionary<string, JsonElement> {
            ["directory_path"] = JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(ctx.WorkingDir)).RootElement.Clone(),
        };
        var listResult = await ctx.Gateway.ExecuteAsync("directory_list", listArgs, CancellationToken.None).ConfigureAwait(true);
        listResult.IsError.Should().BeFalse($"directory_list 应成功: {listResult.Content.FirstOrDefault()?.Text}");

        // 断言列表含写入文件名
        var listText = listResult.Content.FirstOrDefault()?.Text ?? "";
        listText.Should().Contain(fileName, "directory_list 应包含刚写入的文件名（行为验证）");
    }

    /// <summary>
    /// 默认值参数全量验证 — 用 ArgumentGenerator 生成默认值参数，按分类超时调用所有工具
    /// </summary>
    [Fact]
    public async Task All_Tools_With_Default_Arguments_No_Crash() {
        var ctx = await BuildAndRegisterAllToolsAsync().ConfigureAwait(true);
        var gateway = ctx.Gateway;
        var allTools = ctx.Tools;
        var strategy = new DefaultToolValidationStrategy();

        var results = new List<(string ToolName, ToolValidationKind Kind, string Status, string? ErrorMessage)>();
        var crashed = new List<(string ToolName, Exception Ex)>();
        var timedOut = new List<(string ToolName, Exception Ex)>();

        foreach (var tool in allTools.OrderBy(t => t.Name)) {
            var kind = strategy.GetKind(tool);
            if (kind is ToolValidationKind.Skip) {
                results.Add((tool.Name, kind, "SKIPPED", "策略跳过"));
                continue;
            }
            if (kind is ToolValidationKind.MockExecute) {
                results.Add((tool.Name, kind, "SKIPPED", "写类工具需MockServer，DI层跳过"));
                continue;
            }
            if (kind is ToolValidationKind.ValidateOnly) {
                results.Add((tool.Name, kind, "SKIPPED", "危险类仅参数校验，见缺失参数测试"));
                continue;
            }

            try {
                var args = ArgumentGenerator.GenerateDefaultArguments(tool);
                var timeout = ToolTimeoutPolicy.GetTimeout(tool);
                using var cts = new CancellationTokenSource(timeout);
                var result = await gateway.ExecuteAsync(tool.Name, args, cts.Token).ConfigureAwait(true);

                result.Should().NotBeNull($"工具 {tool.Name} 返回 null");
                var status = result.IsError ? "ERROR" : "OK";
                results.Add((tool.Name, kind, status, result.IsError ? result.Content.FirstOrDefault()?.Text : null));
            } catch (OperationCanceledException ex) {
                timedOut.Add((tool.Name, ex));
                results.Add((tool.Name, kind, "TIMEOUT", $"{ToolTimeoutPolicy.GetTimeout(tool).TotalSeconds}s 超时"));
            } catch (TimeoutException ex) {
                timedOut.Add((tool.Name, ex));
                results.Add((tool.Name, kind, "TIMEOUT", $"{ToolTimeoutPolicy.GetTimeout(tool).TotalSeconds}s 超时"));
            } catch (Exception ex) {
                crashed.Add((tool.Name, ex));
                results.Add((tool.Name, kind, "CRASH", $"{ex.GetType().Name}: {ex.Message}"));
            }
        }

        var report = new StringBuilder();
        report.AppendLine("# 默认值参数全量验证报告");
        report.AppendLine($"时间: {DateTimeOffset.UtcNow:O}");
        report.AppendLine($"工具总数: {allTools.Count}");
        report.AppendLine($"成功: {results.Count(r => r.Status == "OK")}");
        report.AppendLine($"错误(非崩溃): {results.Count(r => r.Status == "ERROR")}");
        report.AppendLine($"崩溃: {crashed.Count}");
        report.AppendLine($"超时: {timedOut.Count}");
        report.AppendLine($"跳过: {results.Count(r => r.Status == "SKIPPED")}");
        report.AppendLine();
        report.AppendLine("## 详细结果");
        foreach (var (toolName, kind, status, errorMessage) in results.OrderBy(r => r.ToolName)) {
            var mnemonic = status switch {
                "OK" => "[OK]", "ERROR" => "[ERR]", "CRASH" => "[CRASH]",
                "TIMEOUT" => "[TMO]", "SKIPPED" => "[SKIP]", _ => $"[{status}]"
            };
            var truncatedError = errorMessage switch {
                null => "", _ when errorMessage.Length > 80 => errorMessage[..80] + "...", _ => errorMessage
            };
            report.AppendLine($"{mnemonic} [{kind}] {toolName} {truncatedError}");
        }
        Console.WriteLine(report.ToString());

        crashed.Should().BeEmpty($"以下工具崩溃: {string.Join(", ", crashed.Select(c => c.ToolName))}");
        timedOut.Should().BeEmpty($"以下工具超时: {string.Join(", ", timedOut.Select(c => c.ToolName))}");
    }

    /// <summary>
    /// 缺失必填参数验证 — 逐个移除每个 Required 参数，验证返回明确错误（非静默失败）
    /// </summary>
    [Fact]
    public async Task All_Tools_Missing_Required_Param_Returns_Error() {
        var ctx = await BuildAndRegisterAllToolsAsync().ConfigureAwait(true);
        var gateway = ctx.Gateway;
        var allTools = ctx.Tools;
        var strategy = new DefaultToolValidationStrategy();

        var results = new List<(string ToolName, string MissingParam, string Status, string? ErrorMessage)>();
        var silentFailures = new List<(string ToolName, string MissingParam)>();
        var crashed = new List<(string ToolName, string MissingParam, Exception Ex)>();

        foreach (var tool in allTools.OrderBy(t => t.Name)) {
            var kind = strategy.GetKind(tool);
            if (kind is ToolValidationKind.Skip) continue;

            foreach (var (missingParam, args) in ArgumentGenerator.GenerateMissingParamScenarios(tool)) {
                try {
                    var timeout = ToolTimeoutPolicy.GetTimeout(tool);
                    using var cts = new CancellationTokenSource(timeout);
                    var result = await gateway.ExecuteAsync(tool.Name, args, cts.Token).ConfigureAwait(true);

                    result.Should().NotBeNull($"工具 {tool.Name} 缺失 {missingParam} 返回 null");
                    var status = result.IsError ? "ERROR" : "OK";
                    results.Add((tool.Name, missingParam, status, result.Content.FirstOrDefault()?.Text));

                    if (!result.IsError) {
                        silentFailures.Add((tool.Name, missingParam));
                    }
                } catch (Exception ex) {
                    crashed.Add((tool.Name, missingParam, ex));
                    results.Add((tool.Name, missingParam, "CRASH", $"{ex.GetType().Name}: {ex.Message}"));
                }
            }
        }

        var report = new StringBuilder();
        report.AppendLine("# 缺失必填参数验证报告");
        report.AppendLine($"时间: {DateTimeOffset.UtcNow:O}");
        report.AppendLine($"验证用例总数: {results.Count}");
        report.AppendLine($"返回错误(正确): {results.Count(r => r.Status == "ERROR")}");
        report.AppendLine($"静默成功(错误): {silentFailures.Count}");
        report.AppendLine($"崩溃: {crashed.Count}");
        report.AppendLine();
        if (silentFailures.Count > 0) {
            report.AppendLine("## 静默失败详情（缺失必填参数却返回成功）");
            foreach (var (toolName, missingParam) in silentFailures)
                report.AppendLine($"- {toolName} 缺失 {missingParam}");
        }
        if (crashed.Count > 0) {
            report.AppendLine("## 崩溃详情");
            foreach (var (toolName, missingParam, ex) in crashed.Take(20))
                report.AppendLine($"- {toolName} 缺失 {missingParam}: {ex.GetType().Name}: {ex.Message}");
        }
        Console.WriteLine(report.ToString());

        silentFailures.Should().BeEmpty($"以下工具缺失必填参数却静默成功: {string.Join(", ", silentFailures.Select(s => $"{s.ToolName}缺{s.MissingParam}"))}");
        crashed.Should().BeEmpty($"以下工具缺失参数时崩溃: {string.Join(", ", crashed.Select(c => c.ToolName))}");
    }
}
