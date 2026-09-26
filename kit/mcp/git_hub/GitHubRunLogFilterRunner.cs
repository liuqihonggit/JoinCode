namespace McpToolDispatch;

/// <summary>
/// GitHub Run 日志过滤运行器 — DI 注入 _apiClient,负责失败测试过滤+流式过滤+失败 job 日志获取
/// </summary>
internal sealed class GitHubRunLogFilterRunner {
    private readonly IGitHubApiClient _apiClient;

    /// <summary>
    /// 构造日志过滤运行器,注入 GitHub API 客户端(非 null,调用方负责空检查)
    /// </summary>
    public GitHubRunLogFilterRunner(IGitHubApiClient apiClient) {
        _apiClient = apiClient;
    }

    /// <summary>
    /// 获取指定 Run 中所有失败 job 的日志 — 逐行 yield(合并多个 job 日志)
    /// <para>用于 expand=failed 模式,只拉 conclusion=failure 的 job 日志</para>
    /// </summary>
    public async IAsyncEnumerable<string> GetFailedJobLogsAsync(
        string owner, string repo, string runId,
        [EnumeratorCancellation] CancellationToken ct) {
        var jobsResult = await _apiClient.SendAsync(HttpMethod.Get, $"repos/{owner}/{repo}/actions/runs/{runId}/jobs", paginate: true, ct: ct).ConfigureAwait(false);
        if (!jobsResult.Success) yield break;

        List<long> failedJobIds;
        using (var doc = JsonDocument.Parse(jobsResult.Body)) {
            if (!doc.RootElement.TryGetProperty("jobs", out var jobsEl)) yield break;
            failedJobIds = new List<long>();
            foreach (var job in jobsEl.EnumerateArray()) {
                if (!job.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.Number) continue;
                var conclusion = job.TryGetProperty("conclusion", out var conEl) ? conEl.GetString() : null;
                if (string.Equals(conclusion, "failure", StringComparison.OrdinalIgnoreCase))
                    failedJobIds.Add(idEl.GetInt64());
            }
        }

        await foreach (var line in DownloadJobsParallelAsync(owner, repo, failedJobIds, ct).ConfigureAwait(false)) {
            yield return line;
        }
    }

    /// <summary>
    /// 统一多 job 日志下载 — 单 job 直接 yield,多 job Channel 并行合并(Actor 邮箱模型)
    /// <para>并行时各 job 行交错合并到 Channel,总时间 ≈ max(各 job) 而非 sum</para>
    /// <para>AGENTS.md 死锁处理规范: Actor 邮箱模型(消息传递替代共享锁)</para>
    /// </summary>
    private async IAsyncEnumerable<string> DownloadJobsParallelAsync(
        string owner, string repo, IReadOnlyList<long> jobIds,
        [EnumeratorCancellation] CancellationToken ct) {
        if (jobIds.Count == 0) yield break;

        // 单 job: 直接 yield(避免 Channel 开销)
        if (jobIds.Count == 1) {
            await foreach (var line in _apiClient.GetJobLogsAsync(owner, repo, jobIds[0], ct).ConfigureAwait(false)) {
                yield return line;
            }
            yield break;
        }

        // 多 job: Channel 并行合并(Actor 邮箱模型)
        var channel = Channel.CreateUnbounded<string>();
        var writer = channel.Writer;

        var tasks = jobIds.Select(async jobId => {
            try {
                await foreach (var line in _apiClient.GetJobLogsAsync(owner, repo, jobId, ct).ConfigureAwait(false)) {
                    await writer.WriteAsync(line, ct).ConfigureAwait(false);
                }
            } catch (OperationCanceledException) {
                // 取消: 静默退出,channel 由外部完成
            } catch (Exception ex) {
                await writer.WriteAsync($"[ERROR] job {jobId}: {ex.Message}", CancellationToken.None).ConfigureAwait(false);
            }
        }).ToArray();

        // 后台等待所有完成后关闭 channel(不阻塞调用方 yield)
        _ = Task.Run(async () => {
            try {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            } finally {
                writer.TryComplete();
            }
        });

        await foreach (var line in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false)) {
            yield return line;
        }
    }

    /// <summary>
    /// 智能过滤测试失败行 — 状态机提取 Failed + Error Message + Stack Trace,Rust 风格输出
    /// <para>状态机: Normal → InFailedTest(遇到 Failed/[FAIL]) → InErrorMessage(Error Message:) → InStackTrace(Stack Trace:) → Normal</para>
    /// <para>输出: 每个失败测试用 --> line N 指示, | 管道符标注日志行, = 总结行</para>
    /// </summary>
    public async Task<ToolResult> FilterFailedTestsAsync(
        string owner, string repo, string runId, string? jobId,
        int maxLines, int skipLines, CancellationToken ct) {
        // 获取日志行枚举源(优先失败 job,其次指定 job,最后整个 run)
        IAsyncEnumerable<string> logLines;
        if (string.IsNullOrWhiteSpace(jobId)) {
            logLines = GetFailedJobLogsAsync(owner, repo, runId, ct);
        } else if (long.TryParse(jobId, out var jobIdLong)) {
            logLines = _apiClient.GetJobLogsAsync(owner, repo, jobIdLong, ct);
        } else {
            logLines = _apiClient.GetRunLogsAsync(owner, repo, long.Parse(runId), ct);
        }

        // 状态机解析
        var failures = new List<TestFailureInfo>();
        TestFailureInfo? current = null;
        var state = LogParseState.Normal;
        var lineNumber = 0;

        await foreach (var line in logLines.ConfigureAwait(false)) {
            lineNumber++;
            var content = GitHubRunLogText.StripLogTimestamp(line);

            switch (state) {
                case LogParseState.Normal:
                // 检测测试失败标记: "  Failed xxx [FAIL]" 或 "[xUnit.net] xxx [FAIL]"
                if (content.Contains("[FAIL]", StringComparison.OrdinalIgnoreCase) ||
                    content.StartsWith("  Failed ", StringComparison.OrdinalIgnoreCase)) {
                    current = new TestFailureInfo { StartLine = lineNumber, TestLine = content };
                    failures.Add(current);
                    state = LogParseState.InFailedTest;
                }
                // 检测 ##[error] 行
                else if (content.Contains("##[error]", StringComparison.OrdinalIgnoreCase)) {
                    current = new TestFailureInfo { StartLine = lineNumber, TestLine = content, IsErrorMarker = true };
                    failures.Add(current);
                    state = LogParseState.Normal;
                }
                break;

                case LogParseState.InFailedTest:
                if (content.StartsWith("  Error Message:", StringComparison.OrdinalIgnoreCase)) {
                    state = LogParseState.InErrorMessage;
                } else if (content.StartsWith("  Stack Trace:", StringComparison.OrdinalIgnoreCase)) {
                    state = LogParseState.InStackTrace;
                } else if (content.StartsWith("  Passed ", StringComparison.OrdinalIgnoreCase) ||
                           content.StartsWith("  Failed ", StringComparison.OrdinalIgnoreCase) ||
                           content.Contains("[PASS]", StringComparison.OrdinalIgnoreCase)) {
                    state = LogParseState.Normal;
                    current = null;
                }
                break;

                case LogParseState.InErrorMessage:
                if (content.StartsWith("  Stack Trace:", StringComparison.OrdinalIgnoreCase)) {
                    state = LogParseState.InStackTrace;
                } else if (content.StartsWith("  Passed ", StringComparison.OrdinalIgnoreCase) ||
                           content.StartsWith("  Failed ", StringComparison.OrdinalIgnoreCase)) {
                    state = LogParseState.Normal;
                    current = null;
                } else if (current is not null) {
                    current.ErrorMessageLines.Add(content.Trim());
                }
                break;

                case LogParseState.InStackTrace:
                if (current is not null) {
                    current.StackTraceLines.Add(content);
                }
                if (content.StartsWith("  Passed ", StringComparison.OrdinalIgnoreCase) ||
                    content.StartsWith("  Failed ", StringComparison.OrdinalIgnoreCase) ||
                    content.Contains("--- End of stack trace", StringComparison.OrdinalIgnoreCase)) {
                    state = LogParseState.Normal;
                    current = null;
                }
                break;
            }
        }

        if (failures.Count == 0) {
            return GitHubToolHandlers.Ok("未检测到测试失败行。尝试用 filter=error 看 ##[error] 标记,或 log=true 看完整日志。", $"Run {runId} 测试失败过滤(0 个):");
        }

        // 去重: 同一测试名可能被 [xUnit.net] [FAIL] 和 Failed 两次报告,保留有 ErrorMessage 的那个
        var deduped = new List<TestFailureInfo>(failures.Count);
        var testNameIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var f in failures) {
            if (f.IsErrorMarker) {
                deduped.Add(f);
                continue;
            }
            var testName = TestFailureInfo.ExtractTestName(f.TestLine);
            if (testName is not null && testNameIndex.TryGetValue(testName, out var existingIdx)) {
                // 同名失败已存在,保留 ErrorMessage 更多的
                if (f.ErrorMessageLines.Count > deduped[existingIdx].ErrorMessageLines.Count)
                    deduped[existingIdx] = f;
            } else {
                testNameIndex[testName ?? $"__line_{f.StartLine}"] = deduped.Count;
                deduped.Add(f);
            }
        }
        failures = deduped;

        // Rust 风格输出
        var sb = new StringBuilder();
        var shown = 0;
        foreach (var f in failures.Skip(skipLines)) {
            if (shown >= maxLines) break;
            shown++;
            sb.Append(f.FormatRustStyle());
            sb.Append('\n');
        }

        var prefix = $"Run {runId} 测试失败({failures.Count} 个,显示 {shown} 个)";
        if (skipLines > 0) prefix += $",跳过前 {skipLines} 个";
        if (skipLines + shown < failures.Count)
            sb.Append($"\n... [共 {failures.Count} 个失败,用 skip_lines={skipLines + shown} 续读]");
        return GitHubToolHandlers.Ok(sb.ToString(), prefix);
    }


    /// <summary>
    /// 流式拉取 + 过滤 + 分页跳过(不缓存,用于 --log-failed 或一次性过滤)
    /// <para>日志源: failedOnly=true → 失败 job 日志; jobId 有值 → 单 job 日志; 否则 → 整个 run 日志</para>
    /// </summary>
    public async Task<ToolResult> StreamAndFilterAsync(
        string owner, string repo, string runId, string? jobId, bool failedOnly,
        string scope, FrozenSet<string>? markers, GitHubLogFilter? filterLevel,
        int maxLines, CancellationToken ct, string? hint = null, int skipLines = 0) {
        var matched = new List<string>(maxLines);
        var skipped = 0;
        var lineNumber = 0;

        // 获取日志行枚举源
        IAsyncEnumerable<string> logLines;
        if (failedOnly) {
            logLines = GetFailedJobLogsAsync(owner, repo, runId, ct);
        } else if (!string.IsNullOrWhiteSpace(jobId)) {
            // 支持逗号分隔多个 job_id 并行下载,如 "123,456"(统一走 DownloadJobsParallelAsync)
            var jobIds = jobId.Split(',')
                .Select(s => s.Trim())
                .Select(s => (Ok: long.TryParse(s, out var id), Id: id))
                .Where(x => x.Ok)
                .Select(x => x.Id)
                .ToArray();
            if (jobIds.Length == 0) {
                return GitHubToolHandlers.Fail($"无效的 Job ID: {jobId}");
            }
            logLines = DownloadJobsParallelAsync(owner, repo, jobIds, ct);
        } else if (long.TryParse(runId, out var runIdLong)) {
            logLines = _apiClient.GetRunLogsAsync(owner, repo, runIdLong, ct);
        } else {
            return GitHubToolHandlers.Fail($"无效的 Run ID: {runId}");
        }

        await foreach (var line in logLines.ConfigureAwait(false)) {
            lineNumber++;
            if (markers is not null && !markers.Any(m => line.Contains(m, StringComparison.OrdinalIgnoreCase)))
                continue;
            // 先跳过 skipLines 行(分页续读)
            if (skipped < skipLines) { skipped++; continue; }
            // 加行号前缀,方便定位(去时间戳减少噪音)
            matched.Add($"  L{lineNumber,5}  {GitHubRunLogText.StripLogTimestamp(line)}");
            if (matched.Count >= maxLines) break;
        }
        var prefix = GitHubRunLogFilter.BuildPrefix(runId, scope, filterLevel, matched.Count);
        if (matched.Count == 0)
            return GitHubToolHandlers.Ok(skipLines > 0 ? $"未匹配到更多日志行(已跳过 {skipLines} 行)" : "未匹配到任何日志行", prefix);
        var text = string.Join('\n', matched);
        // 达到 maxLines 说明可能还有更多行,追加续读提示
        if (matched.Count >= maxLines)
            text += $"\n... [可能还有更多行，用 skip_lines={skipLines + maxLines} 续读]";
        if (hint is not null) text += hint;
        if (GitHubRunLogFilter.HasNoStackTrace(text))
            text += GitHubRunLogHints.NoStackTraceHint;
        return GitHubToolHandlers.Ok(text, prefix);
    }

    /// <summary>
    /// 日志解析状态机状态
    /// </summary>
    private enum LogParseState {
        Normal,         // 普通行
        InFailedTest,   // 遇到 Failed/[FAIL],等待 Error Message 或 Stack Trace
        InErrorMessage, // 在 Error Message: 之后
        InStackTrace,   // 在 Stack Trace: 之后
    }

    /// <summary>
    /// 测试失败信息 — 用于 Rust 风格输出
    /// </summary>
    private sealed class TestFailureInfo {
        public int StartLine;
        public string TestLine = "";
        public List<string> ErrorMessageLines = [];
        public List<string> StackTraceLines = [];
        public bool IsErrorMarker;

        /// <summary>
        /// Rust 风格格式化 — --> line N 指示, | 管道符标注日志行, = 总结行
        /// </summary>
        public string FormatRustStyle() {
            var sb = new StringBuilder();
            sb.Append($"--> line {StartLine}");
            sb.Append('\n');
            sb.Append("   |");
            sb.Append('\n');
            // ##[error] 行: 去掉 ##[error] 前缀,只保留实际错误信息
            var displayLine = TestLine.Trim();
            if (IsErrorMarker && displayLine.StartsWith("##[error]", StringComparison.OrdinalIgnoreCase))
                displayLine = displayLine["##[error]".Length..].Trim();
            sb.Append($"   | {displayLine}");
            sb.Append('\n');
            if (ErrorMessageLines.Count > 0) {
                sb.Append("   |   Error Message:");
                sb.Append('\n');
                foreach (var em in ErrorMessageLines) {
                    sb.Append($"   |     {em}");
                    sb.Append('\n');
                }
            }
            if (StackTraceLines.Count > 0) {
                sb.Append("   |   Stack Trace:");
                sb.Append('\n');
                foreach (var st in StackTraceLines.Take(10)) {
                    sb.Append($"   | {st.Trim()}");
                    sb.Append('\n');
                }
                if (StackTraceLines.Count > 10)
                    sb.Append($"   | ... ({StackTraceLines.Count - 10} 行未显示)");
            }
            sb.Append("   |");
            sb.Append('\n');
            // 总结行
            if (!IsErrorMarker && ErrorMessageLines.Count > 0) {
                var testName = ExtractTestName(TestLine);
                if (testName is not null)
                    sb.Append($"   = test: {testName}");
                else
                    sb.Append("   = (见上方日志行)");
                sb.Append('\n');
                sb.Append($"   = reason: {ErrorMessageLines[0]}");
                sb.Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>从测试失败行中提取测试名称。</summary>
        /// <param name="line">测试失败日志行</param>
        /// <returns>测试名称，提取失败时返回 null</returns>
        public static string? ExtractTestName(string line) {
            // 先去掉时间戳前缀 "2026-09-07T17:09:52.2828405Z content"
            var content = GitHubRunLogText.StripLogTimestamp(line).TrimStart();
            // "  Failed Mcp.Tests.xxx [24 ms]" → "Mcp.Tests.xxx"
            // "[xUnit.net 00:00:00.81]     Mcp.Tests.xxx [FAIL]" → "Mcp.Tests.xxx"
            if (content.StartsWith("Failed ", StringComparison.OrdinalIgnoreCase))
                content = content[7..];
            if (content.StartsWith("[xUnit.net", StringComparison.OrdinalIgnoreCase)) {
                var bracketEnd = content.IndexOf(']');
                if (bracketEnd > 0) content = content[(bracketEnd + 1)..].TrimStart();
            }
            // 截取到 [ 之前
            var bracketIdx = content.IndexOf('[');
            if (bracketIdx > 0) content = content[..bracketIdx].Trim();
            return string.IsNullOrEmpty(content) ? null : content;
        }
    }
}