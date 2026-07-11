namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.Insights, Description = "AI生成会话洞察分析", Usage = "/insights [stats|deep|report]", Category = ChatCommandCategory.Info)]
public sealed class InsightsCommand : IChatCommand
{
    private readonly IClockService _clock = SystemClockService.Instance;
    public string Name => ChatCommandNameConstants.Insights;
    public string Description => "AI生成会话洞察分析";
    public string Usage => "/insights [stats|deep|report]";
    public string[] Aliases => [];
    public string ArgumentHint => "[stats|deep|report]";
    public bool IsHidden => false;

    public async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var args = ChatCommandBase.GetNormalizedArgs(context).ToLowerInvariant();

        if (args is "stats" or "s")
        {
            await ShowStatsAsync(context).ConfigureAwait(false);
        }
        else if (args is "deep" or "d")
        {
            await DeepInsightsAsync(context).ConfigureAwait(false);
        }
        else if (args is "report" or "r")
        {
            await GenerateReportAsync(context).ConfigureAwait(false);
        }
        else
        {
            await AiInsightsAsync(context).ConfigureAwait(false);
        }

        return ChatCommandResult.Continue();
    }

    /// <summary>
    /// 跨会话统计模式 — 对齐 TS insights.ts aggregateData + generateUsageReport 统计部分
    /// </summary>
    private async Task ShowStatsAsync(ChatCommandContext context)
    {
        var scanner = ChatCommandBase.GetService<IInsightSessionScanner>(context);
        if (scanner is null)
        {
            // 回退到当前会话统计
            ShowCurrentSessionStats(context);
            return;
        }

        TerminalHelper.WriteLine("正在扫描会话文件...");
        TerminalHelper.NewLine();

        try
        {
            var sessions = await scanner.ScanAllSessionsAsync(context.CancellationToken).ConfigureAwait(false);

            if (sessions.Count == 0)
            {
                TerminalHelper.WriteLine("未找到会话记录。");
                TerminalHelper.NewLine();
                TerminalHelper.WriteLine("使用 /insights (不带stats) 获取AI生成的洞察分析");
                return;
            }

            var aggregated = InsightDataAggregator.Aggregate(sessions);
            var report = InsightDataAggregator.FormatStatsReport(aggregated);

            TerminalHelper.WriteLine(report);
        }
        catch (OperationCanceledException)
        {
            TerminalHelper.WriteLine("扫描已取消。");
        }
        catch (Exception ex)
        {
            TerminalHelper.WriteLine($"扫描会话失败: {ex.Message}");
            TerminalHelper.NewLine();
            // 回退到当前会话统计
            ShowCurrentSessionStats(context);
        }
    }

    /// <summary>
    /// 当前会话统计（回退模式，scanner 不可用时使用）
    /// </summary>
    private void ShowCurrentSessionStats(ChatCommandContext context)
    {
        var sessionDuration = _clock.GetUtcNow() - context.SessionStartedAt;
        var costTracker = context.Services!.CostTracker;

        TerminalHelper.WriteLine("当前会话统计:");
        TerminalHelper.NewLine();
        TerminalHelper.WriteLine($"  会话时长: {sessionDuration.TotalMinutes:F1} 分钟");
        TerminalHelper.WriteLine($"  会话ID: {context.SessionId}");

        if (costTracker is not null)
        {
            try
            {
                var stats = costTracker.GetTodayStatistics();
                TerminalHelper.WriteLine($"  输入 Token: {stats.PromptTokens:N0}");
                TerminalHelper.WriteLine($"  输出 Token: {stats.CompletionTokens:N0}");
                TerminalHelper.WriteLine($"  总 Token: {stats.PromptTokens + stats.CompletionTokens:N0}");
                TerminalHelper.WriteLine($"  估算成本: ${stats.TotalCostUsd:F4}");
            }
            catch
            {
                TerminalHelper.WriteLine("  (成本数据暂不可用)");
            }
        }
        else
        {
            TerminalHelper.WriteLine("  (成本追踪器不可用)");
        }

        TerminalHelper.NewLine();
        TerminalHelper.WriteLine("使用 /insights (不带stats) 获取AI生成的洞察分析");
    }

    /// <summary>
    /// AI 洞察模式 — 对齐 TS insights.ts getPromptForCommand
    /// TS 中此命令类型为 'prompt'，返回 prompt 让 LLM 回复
    /// C# 端简化为直接发送 prompt 给 ChatService
    /// </summary>
    private async Task AiInsightsAsync(ChatCommandContext context)
    {
        var scanner = ChatCommandBase.GetService<IInsightSessionScanner>(context);
        string dataContext;

        if (scanner is not null)
        {
            try
            {
                TerminalHelper.WriteLine("正在分析会话数据...");
                var sessions = await scanner.ScanAllSessionsAsync(context.CancellationToken).ConfigureAwait(false);

                if (sessions.Count > 0)
                {
                    var aggregated = InsightDataAggregator.Aggregate(sessions);
                    dataContext = BuildCrossSessionPrompt(aggregated, sessions.Count);
                }
                else
                {
                    dataContext = BuildCurrentSessionPrompt(context);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                dataContext = BuildCurrentSessionPrompt(context);
            }
        }
        else
        {
            dataContext = BuildCurrentSessionPrompt(context);
        }

        TerminalHelper.WriteLine("正在生成会话洞察...");
        TerminalHelper.NewLine();

        await context.Services!.ChatService.SendMessageAsync(dataContext, context.CancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 深度洞察模式 — Phase2: Facet提取 + 并行Insight生成
    /// 对齐 TS insights.ts generateUsageReport 的完整流程
    /// </summary>
    private async Task DeepInsightsAsync(ChatCommandContext context)
    {
        var scanner = ChatCommandBase.GetService<IInsightSessionScanner>(context);
        var facetCache = ChatCommandBase.GetService<IFacetCacheService>(context);

        if (scanner is null)
        {
            TerminalHelper.WriteLine("会话扫描服务不可用，无法生成深度洞察。");
            TerminalHelper.WriteLine("使用 /insights (不带deep) 获取基础AI洞察分析");
            return;
        }

        try
        {
            // Step 1: 扫描会话
            TerminalHelper.WriteLine("正在扫描会话文件...");
            var sessions = await scanner.ScanAllSessionsAsync(context.CancellationToken).ConfigureAwait(false);

            if (sessions.Count == 0)
            {
                TerminalHelper.WriteLine("未找到会话记录。");
                return;
            }

            TerminalHelper.WriteLine($"找到 {sessions.Count} 个会话");

            // Step 2: 聚合数据
            var aggregated = InsightDataAggregator.Aggregate(sessions);

            // Step 3: Facet 提取（带缓存）
            TerminalHelper.WriteLine("正在提取会话 Facet...");
            var facets = await ExtractFacetsAsync(sessions, facetCache, context).ConfigureAwait(false);

            // Step 4: 聚合 Facet
            var facetSummary = facets.Count > 0 ? FacetAggregator.Aggregate(facets) : null;

            // Step 5: Multi-Clauding 检测
            var multiClauding = MultiClaudingDetector.Detect(sessions);

            // Step 6: 构建数据上下文
            var dataContext = InsightPrompts.BuildInsightDataContext(aggregated, facetSummary, multiClauding);

            // Step 7: 生成并行 Insight（通过 ChatService）
            TerminalHelper.WriteLine("正在生成深度洞察...");
            TerminalHelper.NewLine();

            var deepPrompt = BuildDeepInsightPrompt(dataContext, aggregated, facetSummary, multiClauding);
            await context.Services!.ChatService.SendMessageAsync(deepPrompt, context.CancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TerminalHelper.WriteLine("深度洞察生成已取消。");
        }
        catch (Exception ex)
        {
            TerminalHelper.WriteLine($"生成深度洞察失败: {ex.Message}");
            TerminalHelper.NewLine();
            TerminalHelper.WriteLine("使用 /insights (不带deep) 获取基础AI洞察分析");
        }
    }

    /// <summary>
    /// 生成 HTML 报告 — Phase3: 对齐 TS insights.ts generateHtmlReport
    /// 生成 HTML 文件保存到 ~/.jcc/usage-data/report.html
    /// </summary>
    private async Task GenerateReportAsync(ChatCommandContext context)
    {
        var scanner = ChatCommandBase.GetService<IInsightSessionScanner>(context);
        var facetCache = ChatCommandBase.GetService<IFacetCacheService>(context);

        if (scanner is null)
        {
            TerminalHelper.WriteLine("会话扫描服务不可用，无法生成报告。");
            return;
        }

        try
        {
            // Step 1: 扫描会话
            TerminalHelper.WriteLine("正在扫描会话文件...");
            var sessions = await scanner.ScanAllSessionsAsync(context.CancellationToken).ConfigureAwait(false);

            if (sessions.Count == 0)
            {
                TerminalHelper.WriteLine("未找到会话记录。");
                return;
            }

            TerminalHelper.WriteLine($"找到 {sessions.Count} 个会话");

            // Step 2: 聚合数据
            var aggregated = InsightDataAggregator.Aggregate(sessions);

            // Step 3: Facet 提取（带缓存）
            TerminalHelper.WriteLine("正在提取会话 Facet...");
            var facets = await ExtractFacetsAsync(sessions, facetCache, context).ConfigureAwait(false);
            var facetSummary = facets.Count > 0 ? FacetAggregator.Aggregate(facets) : null;

            // Step 4: Multi-Clauding 检测
            var multiClauding = MultiClaudingDetector.Detect(sessions);

            // Step 5: 生成 AI 洞察文本
            TerminalHelper.WriteLine("正在生成洞察文本...");
            var dataContext = InsightPrompts.BuildInsightDataContext(aggregated, facetSummary, multiClauding);
            var deepPrompt = BuildDeepInsightPrompt(dataContext, aggregated, facetSummary, multiClauding);
            var insightsText = await context.Services!.ChatService.SendMessageAsync(deepPrompt, context.CancellationToken).ConfigureAwait(false);

            // Step 6: 生成 HTML 报告
            TerminalHelper.WriteLine("正在生成 HTML 报告...");
            var html = InsightHtmlReport.Generate(aggregated, facetSummary, multiClauding, insightsText ?? string.Empty);

            // Step 7: 保存到文件
            var reportDir = Path.Combine(
                WorkflowConstants.Paths.JccDirectory,
                "usage-data");
            var fs = context.Services!.FileSystem;
            DirectoryHelper.EnsureDirectoryExists(fs, reportDir);

            var reportPath = Path.Combine(reportDir, "report.html");
            await fs.WriteAllTextAsync(reportPath, html, context.CancellationToken).ConfigureAwait(false);

            TerminalHelper.NewLine();
            TerminalHelper.WriteLine($"HTML 报告已生成: {reportPath}");

            // 同时输出终端统计
            var report = InsightDataAggregator.FormatStatsReport(aggregated);
            TerminalHelper.NewLine();
            TerminalHelper.WriteLine(report);
        }
        catch (OperationCanceledException)
        {
            TerminalHelper.WriteLine("报告生成已取消。");
        }
        catch (Exception ex)
        {
            TerminalHelper.WriteLine($"生成报告失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 提取所有会话的 Facet（带缓存）— 对齐 TS extractFacetsFromAPI + 缓存逻辑
    /// </summary>
    private static async Task<List<SessionFacets>> ExtractFacetsAsync(
        IReadOnlyList<InsightSessionMeta> sessions,
        IFacetCacheService? facetCache,
        ChatCommandContext context)
    {
        var facets = new List<SessionFacets>();
        var chatService = context.Services!.ChatService;

        foreach (var session in sessions)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            // 尝试从缓存加载
            if (facetCache is not null)
            {
                try
                {
                    var cached = await facetCache.LoadAsync(session.SessionId, context.CancellationToken).ConfigureAwait(false);
                    if (cached is not null)
                    {
                        facets.Add(cached);
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    // 缓存读取失败，继续提取
                    System.Diagnostics.Trace.WriteLine($"Facet缓存读取失败: {ex.Message}");
                }
            }

            // 通过 LLM 提取 Facet — 对齐 TS extractFacetsFromAPI
            try
            {
                var transcriptText = BuildTranscriptText(session);
                if (string.IsNullOrWhiteSpace(transcriptText)) continue;

                // 长会话摘要 — 对齐 TS: >30000 字符时分块摘要
                if (transcriptText.Length > 30000)
                {
                    transcriptText = await SummarizeLongTranscriptAsync(transcriptText, chatService, context.CancellationToken).ConfigureAwait(false);
                }

                var prompt = InsightPrompts.BuildFacetExtractionPrompt(transcriptText);
                var response = await chatService.SendMessageAsync(prompt, context.CancellationToken).ConfigureAwait(false);

                var facet = ParseFacetResponse(response, session.SessionId);
                if (facet is not null)
                {
                    facets.Add(facet);

                    // 保存到缓存
                    if (facetCache is not null)
                    {
                        try
                        {
                            await facetCache.SaveAsync(facet, context.CancellationToken).ConfigureAwait(false);
                        }
                        catch (Exception ex2)
                        {
                            // 缓存保存失败不影响主流程
                            System.Diagnostics.Trace.WriteLine($"Facet缓存保存失败: {ex2.Message}");
                        }
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                // 单个会话 Facet 提取失败不影响其他会话
                System.Diagnostics.Trace.WriteLine($"Facet提取失败: {ex.Message}");
            }
        }

        return facets;
    }

    /// <summary>
    /// 从 InsightSessionMeta 构建转录文本
    /// </summary>
    private static string BuildTranscriptText(InsightSessionMeta session)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"Session: {session.SessionId}");
        sb.AppendLine($"Duration: {session.DurationMinutes:F1} minutes");
        sb.AppendLine($"User messages: {session.UserMessageCount}");
        sb.AppendLine($"Assistant messages: {session.AssistantMessageCount}");
        sb.AppendLine($"First prompt: {session.FirstPrompt}");

        if (session.ToolCounts.Count > 0)
        {
            sb.AppendLine("Tools used:");
            foreach (var (tool, count) in session.ToolCounts.OrderByDescending(kvp => kvp.Value))
            {
                sb.AppendLine($"  {tool}: {count}");
            }
        }

        if (session.Languages.Count > 0)
        {
            sb.AppendLine("Languages:");
            foreach (var (lang, count) in session.Languages.OrderByDescending(kvp => kvp.Value).Take(5))
            {
                sb.AppendLine($"  {lang}: {count}");
            }
        }

        if (session.GitCommits > 0) sb.AppendLine($"Git commits: {session.GitCommits}");
        if (session.UserInterruptions > 0) sb.AppendLine($"User interruptions: {session.UserInterruptions}");
        if (session.ToolErrors > 0) sb.AppendLine($"Tool errors: {session.ToolErrors}");

        return sb.ToString();
    }

    /// <summary>
    /// 长会话摘要 — 对齐 TS summarizeTranscriptChunk
    /// 按 25000 字符分块，每块独立摘要后拼接
    /// </summary>
    private static async Task<string> SummarizeLongTranscriptAsync(
        string transcriptText,
        IChatService chatService,
        CancellationToken cancellationToken)
    {
        const int chunkSize = 25000;
        var chunks = new List<string>();

        for (var i = 0; i < transcriptText.Length; i += chunkSize)
        {
            var length = Math.Min(chunkSize, transcriptText.Length - i);
            chunks.Add(transcriptText[i..(i + length)]);
        }

        // 限制最多5个块 — 避免过多 LLM 调用
        chunks = chunks.Take(5).ToList();

        var summaries = new List<string>();
        foreach (var chunk in chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var prompt = InsightPrompts.BuildTranscriptSummaryPrompt(chunk);
                var summary = await chatService.SendMessageAsync(prompt, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(summary))
                {
                    summaries.Add(summary);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                // 单个块摘要失败不影响其他块
                System.Diagnostics.Trace.WriteLine($"会话摘要块处理失败: {ex.Message}");
            }
        }

        return string.Join("\n\n", summaries);
    }

    /// <summary>
    /// 解析 LLM 返回的 Facet JSON — 对齐 TS isValidSessionFacets 校验
    /// </summary>
    private static SessionFacets? ParseFacetResponse(string response, string sessionId)
    {
        try
        {
            // 清理 markdown fences
            var json = response.Trim();
            if (json.StartsWith("```json")) json = json[7..];
            if (json.StartsWith("```")) json = json[3..];
            if (json.EndsWith("```")) json = json[..^3];
            json = json.Trim();

            var facet = JsonSerializer.Deserialize(json, SessionFacetsJsonContext.Default.SessionFacets);
            if (facet is null) return null;

            // 确保 SessionId 正确 — SessionFacets 是 sealed class，不能使用 with
            if (facet.SessionId != sessionId)
            {
                return new SessionFacets
                {
                    SessionId = sessionId,
                    UnderlyingGoal = facet.UnderlyingGoal,
                    GoalCategories = facet.GoalCategories,
                    Outcome = facet.Outcome,
                    UserSatisfactionCounts = facet.UserSatisfactionCounts,
                    ClaudeHelpfulness = facet.ClaudeHelpfulness,
                    SessionType = facet.SessionType,
                    FrictionCounts = facet.FrictionCounts,
                    FrictionDetail = facet.FrictionDetail,
                    PrimarySuccess = facet.PrimarySuccess,
                    BriefSummary = facet.BriefSummary,
                    UserInstructionsToClaude = facet.UserInstructionsToClaude,
                };
            }

            return facet;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 构建深度洞察 prompt — 整合所有数据让 LLM 生成综合分析
    /// </summary>
    private static string BuildDeepInsightPrompt(
        string dataContext,
        AggregatedInsightData data,
        FacetSummary? facets,
        MultiClaudingResult? multiClauding)
    {
        var sb = new StringBuilder();

        sb.AppendLine("You are generating a comprehensive Claude Code Insights report.");
        sb.AppendLine();
        sb.AppendLine(dataContext);
        sb.AppendLine();

        sb.AppendLine("Generate a comprehensive insights report covering ALL of the following sections:");
        sb.AppendLine();

        sb.AppendLine("1. PROJECT AREAS: Identify 4-5 main project areas the user worked on");
        sb.AppendLine("2. INTERACTION STYLE: Analyze the user's interaction patterns with Claude Code");
        sb.AppendLine("3. WHAT WORKS: Identify 3 impressive workflows that worked well");
        sb.AppendLine("4. FRICTION ANALYSIS: Identify 3 friction categories with examples");
        sb.AppendLine("5. SUGGESTIONS: Provide improvement suggestions (CLAUDE.md additions, features to try, usage patterns)");
        sb.AppendLine("6. ON THE HORIZON: Identify 3 future opportunities with copyable prompts");
        sb.AppendLine("7. FUN ENDING: Find an interesting or amusing moment");

        if (multiClauding is not null && multiClauding.OverlapEvents > 0)
        {
            sb.AppendLine("8. MULTI-SESSION USAGE: Analyze the parallel session usage patterns");
        }

        sb.AppendLine();
        sb.AppendLine("Format the report with clear section headers and use second person ('you').");
        sb.AppendLine("Be specific, actionable, and concise.");
        sb.AppendLine("Include copyable prompts where appropriate.");

        return sb.ToString();
    }

    /// <summary>
    /// 构建跨会话分析 prompt — 对齐 TS insights.ts getPromptForCommand 中的 prompt 构建
    /// </summary>
    private static string BuildCrossSessionPrompt(AggregatedInsightData data, int sessionCount)
    {
        var topTools = data.ToolCounts
            .OrderByDescending(kvp => kvp.Value)
            .Take(8)
            .Select(kvp => $"  {kvp.Key}: {kvp.Value}")
            .Join("\n");

        var topLanguages = data.Languages
            .OrderByDescending(kvp => kvp.Value)
            .Take(5)
            .Select(kvp => $"  {kvp.Key}: {kvp.Value}")
            .Join("\n");

        return $"""
            Analyze the following Claude Code usage data and provide insights. Data from {sessionCount} sessions:

            Overview:
            - Sessions: {data.TotalSessions}
            - Date range: {data.StartDate:yyyy-MM-dd} to {data.EndDate:yyyy-MM-dd}
            - Total messages: {data.TotalMessages:N0}
            - Total hours: {data.TotalDurationHours:F1}
            - Days active: {data.DaysActive}
            - Messages per day: {data.MessagesPerDay:F1}

            Token usage:
            - Input: {data.TotalInputTokens:N0}
            - Output: {data.TotalOutputTokens:N0}
            - Total: {data.TotalInputTokens + data.TotalOutputTokens:N0}

            Top tools:
            {topTools}

            Languages:
            {topLanguages}

            Code changes:
            - Lines added: {data.TotalLinesAdded:N0}
            - Lines removed: {data.TotalLinesRemoved:N0}
            - Files modified: {data.TotalFilesModified}
            - Git commits: {data.GitCommits}

            Provide a concise analysis covering:
            1. Session productivity assessment
            2. Token usage patterns and efficiency
            3. Tool usage patterns and suggestions
            4. Code change patterns
            5. Suggestions for improving workflow

            Keep the response concise and actionable.
            """;
    }

    /// <summary>
    /// 构建当前会话分析 prompt（回退模式）
    /// </summary>
    private string BuildCurrentSessionPrompt(ChatCommandContext context)
    {
        var sessionDuration = _clock.GetUtcNow() - context.SessionStartedAt;
        var tokenInfo = GetTokenInfo(context);

        return $"""
            Analyze the current session and provide insights. Session data:
            - Duration: {sessionDuration.TotalMinutes:F1} minutes
            - Session ID: {context.SessionId}
            {tokenInfo}

            Provide a concise analysis covering:
            1. Session productivity assessment
            2. Token usage patterns and efficiency
            3. Suggestions for improving workflow
            4. Key observations about the conversation

            Keep the response concise and actionable.
            """;
    }

    private static string GetTokenInfo(ChatCommandContext context)
    {
        var costTracker = context.Services!.CostTracker;
        if (costTracker is null) return "- Token data: unavailable";

        try
        {
            var stats = costTracker.GetTodayStatistics();
            return $"- Input tokens: {stats.PromptTokens:N0}\n- Output tokens: {stats.CompletionTokens:N0}\n- Total tokens: {stats.PromptTokens + stats.CompletionTokens:N0}\n- Estimated cost: ${stats.TotalCostUsd:F4}";
        }
        catch
        {
            return "- Token data: unavailable";
        }
    }
}

/// <summary>
/// LINQ 风格的字符串连接扩展
/// </summary>
file static class InsightsStringExtensions
{
    public static string Join(this IEnumerable<string> source, string separator) =>
        string.Join(separator, source);
}
