namespace IO.ProcessService;

/// <summary>
/// GitHub CLI 命令统一执行器 — 封装 gh 命令，支持 PR body 自动生成和重试机制
/// <para>
/// 核心价值：
/// 1. 统一 gh 进程调用（编码、环境变量、错误处理）
/// 2. 强制走 IProcessService（消除直接 ProcessStartInfo 绕过安全检查的隐患）
/// 3. PR body 自动生成（避免用户忘记填写）
/// 4. 重试机制（指数退避，解决网络超时问题）
/// </para>
/// </summary>
[Register(typeof(IGitHubCommandRunner), ServiceLifetime.Singleton)]
public sealed partial class GitHubCommandRunner : ServiceEntity, IGitHubCommandRunner
{
    private readonly IProcessService _processService;
    private readonly PrBodyGenerator _prBodyGenerator;
    private readonly ILogger<GitHubCommandRunner>? _logger;

    // 重试配置
    private readonly int _maxRetries = 3;
    private readonly TimeSpan _initialBackoff = TimeSpan.FromSeconds(1);
    private readonly TimeSpan _maxBackoff = TimeSpan.FromSeconds(10);
    private readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);

    public GitHubCommandRunner(
        IProcessService processService,
        PrBodyGenerator prBodyGenerator,
        ILogger<GitHubCommandRunner>? logger = null)
    {
        _processService = processService ?? throw new ArgumentNullException(nameof(processService));
        _prBodyGenerator = prBodyGenerator ?? throw new ArgumentNullException(nameof(prBodyGenerator));
        _logger = logger;
    }

    /// <summary>
    /// 执行 gh 命令
    /// </summary>
    public async Task<GitHubCommandResult> ExecuteAsync(
        string arguments,
        string? workingDirectory = null,
        CancellationToken ct = default)
    {
        try
        {
            Console.Error.WriteLine($"[DIAG-GH] ExecuteAsync start: gh {arguments}, cwd={workingDirectory}");
            Console.Error.Flush();

            var options = new ProcessOptions
            {
                FileName = "gh",
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                TimeoutMs = (int)_timeout.TotalMilliseconds,
                EnvironmentVariables = CreateGitHubEnvironment(),
                SkipArgumentValidation = true
            };

            var result = await _processService.ExecuteAsync(options, ct).ConfigureAwait(false);

            Console.Error.WriteLine($"[DIAG-GH] ExecuteAsync end: gh {arguments}, exitCode={result.ExitCode}, stdoutLen={result.StandardOutput.Length}");
            Console.Error.Flush();

            return new GitHubCommandResult
            {
                Success = result.Success,
                Output = result.StandardOutput,
                Error = result.StandardError,
                ExitCode = result.ExitCode
            };
        }
        catch (OperationCanceledException ex)
        {
            Console.Error.WriteLine($"[DIAG-GH] ExecuteAsync CANCELED: gh {arguments}, {ex.GetType().Name}");
            Console.Error.Flush();
            throw;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DIAG-GH] ExecuteAsync EXCEPTION: gh {arguments}, {ex.GetType().Name}: {ex.Message}");
            Console.Error.Flush();
            _logger?.LogError(ex, "执行 GitHub 命令失败: gh {Arguments}", arguments);
            return new GitHubCommandResult
            {
                Success = false,
                Error = ex.Message,
                ExitCode = -1
            };
        }
    }

    /// <summary>
    /// 创建 PR — 自动注入 body 参数，支持重试机制
    /// </summary>
    public async Task<PrCreateResult> CreatePrAsync(
        string title,
        string? body,
        string baseBranch,
        string headBranch,
        string? repo = null,
        bool draft = false,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(baseBranch);
        ArgumentNullException.ThrowIfNull(headBranch);

        // 如果 body 为空，自动生成
        var finalBody = string.IsNullOrWhiteSpace(body)
            ? await _prBodyGenerator.GenerateFromCommitsAsync(baseBranch, headBranch, null, ct).ConfigureAwait(false)
            : body;

        // 记录"已用默认值"
        if (string.IsNullOrWhiteSpace(body))
        {
            _logger?.LogInformation("PR body 为空，已自动生成: {Title}", title);
            Console.Error.WriteLine($"[DIAG-GH] PR body 自动生成，已用默认值");
            Console.Error.Flush();
        }

        // 构建命令参数
        var sb = new StringBuilder();
        sb.Append($"pr create --title \"{EscapeArg(title)}\" --body \"{EscapeArg(finalBody)}\" --base {baseBranch} --head {headBranch}");

        if (draft)
        {
            sb.Append(" --draft");
        }

        if (!string.IsNullOrWhiteSpace(repo))
        {
            sb.Append($" --repo {repo}");
        }

        // 带重试执行
        var result = await ExecuteWithRetryAsync(sb.ToString(), null, ct).ConfigureAwait(false);

        if (!result.Success)
        {
            return new PrCreateResult
            {
                Success = false,
                Error = result.Error
            };
        }

        // 解析输出，提取 PR URL
        var output = result.Output.Trim();
        var prUrl = ParsePrUrl(output);
        var prNumber = ParsePrNumber(output);

        return new PrCreateResult
        {
            Success = true,
            PrUrl = prUrl,
            PrNumber = prNumber
        };
    }

    /// <summary>
    /// 列出 PR
    /// </summary>
    public async Task<PrListResult> ListPrsAsync(
        string? repo = null,
        string state = "open",
        int limit = 30,
        CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        sb.Append($"pr list --state {state} --limit {limit}");

        if (!string.IsNullOrWhiteSpace(repo))
        {
            sb.Append($" --repo {repo}");
        }

        // 带重试执行
        var result = await ExecuteWithRetryAsync(sb.ToString(), null, ct).ConfigureAwait(false);

        if (!result.Success)
        {
            return new PrListResult
            {
                Success = false,
                Error = result.Error
            };
        }

        // 解析输出
        var items = ParsePrList(result.Output);

        return new PrListResult
        {
            Success = true,
            Items = items
        };
    }

    /// <summary>
    /// 带重试执行（指数退避）
    /// </summary>
    private async Task<GitHubCommandResult> ExecuteWithRetryAsync(
        string arguments,
        string? workingDirectory,
        CancellationToken ct)
    {
        var retryCount = 0;
        var lastError = string.Empty;

        while (retryCount <= _maxRetries)
        {
            try
            {
                var result = await ExecuteAsync(arguments, workingDirectory, ct).ConfigureAwait(false);

                if (result.Success)
                {
                    return result;
                }

                lastError = result.Error;

                // 检查是否可重试的错误
                if (!IsRetryableError(result.Error))
                {
                    return result;
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                lastError = "超时";
                // 超时，可重试
            }
            catch (Exception ex)
            {
                lastError = ex.Message;

                // 非网络异常，不重试
                if (!IsRetryableException(ex))
                {
                    throw;
                }
            }

            // 重试逻辑
            if (retryCount < _maxRetries)
            {
                var delay = CalculateBackoff(retryCount);
                _logger?.LogWarning(
                    "GitHub 命令执行失败（第 {RetryCount} 次），{Delay}ms 后重试: {Error}",
                    retryCount + 1,
                    delay.TotalMilliseconds,
                    lastError);

                await Task.Delay(delay, ct).ConfigureAwait(false);
            }

            retryCount++;
        }

        return new GitHubCommandResult
        {
            Success = false,
            Error = $"重试 {_maxRetries} 次后仍然失败: {lastError}"
        };
    }

    /// <summary>
    /// 计算指数退避时间
    /// </summary>
    private TimeSpan CalculateBackoff(int retryCount)
    {
        var delay = TimeSpan.FromSeconds(
            Math.Min(
                _initialBackoff.TotalSeconds * Math.Pow(2, retryCount),
                _maxBackoff.TotalSeconds));

        // 添加随机抖动（避免惊群效应）
        var jitter = Random.Shared.NextDouble() * 0.5; // 0-50% 随机抖动
        return delay + TimeSpan.FromSeconds(jitter);
    }

    /// <summary>
    /// 判断错误是否可重试
    /// </summary>
    private static bool IsRetryableError(string error)
    {
        // 网络相关错误可重试
        var retryablePatterns = new[]
        {
            "timeout",
            "timeout",
            "network",
            "connection",
            "rate limit",
            "rate limit",
            "try again"
        };

        var errorLower = error.ToLowerInvariant();
        return retryablePatterns.Any(p => errorLower.Contains(p));
    }

    /// <summary>
    /// 判断异常是否可重试
    /// </summary>
    private static bool IsRetryableException(Exception ex)
    {
        return ex is OperationCanceledException
               || ex is TimeoutException
               || ex is System.Net.Http.HttpRequestException;
    }

    /// <summary>
    /// 解析 PR URL
    /// </summary>
    private static string? ParsePrUrl(string output)
    {
        // gh pr create 输出格式：https://github.com/owner/repo/pull/123
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("http", StringComparison.OrdinalIgnoreCase) &&
                trimmed.Contains("/pull/"))
            {
                return trimmed;
            }
        }

        return null;
    }

    /// <summary>
    /// 解析 PR 编号
    /// </summary>
    private static string? ParsePrNumber(string output)
    {
        var url = ParsePrUrl(output);
        if (url is null)
        {
            return null;
        }

        // 从 URL 中提取 PR 编号
        var lastSlash = url.LastIndexOf('/');
        if (lastSlash >= 0 && lastSlash < url.Length - 1)
        {
            return url.Substring(lastSlash + 1);
        }

        return null;
    }

    /// <summary>
    /// 解析 PR 列表
    /// </summary>
    private static IReadOnlyList<PrListItem> ParsePrList(string output)
    {
        var items = new List<PrListItem>();

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            // gh pr list 输出格式：123\tTitle\tbranch\tSTATE
            var parts = line.Split('\t');
            if (parts.Length >= 4)
            {
                items.Add(new PrListItem
                {
                    Number = parts[0].Trim(),
                    Title = parts[1].Trim(),
                    Branch = parts[2].Trim(),
                    State = parts[3].Trim(),
                    Url = $"https://github.com/{parts[2].Trim()}/pull/{parts[0].Trim()}"
                });
            }
        }

        return items;
    }

    /// <summary>
    /// 转义命令行参数
    /// </summary>
    private static string EscapeArg(string arg)
    {
        return arg.Replace("\"", "\\\"");
    }

    /// <summary>
    /// 创建 GitHub CLI 专用环境变量
    /// </summary>
    private static Dictionary<string, string> CreateGitHubEnvironment() => new()
    {
        ["GH_TERMINAL_PROMPT"] = "0",
        ["GH_FORCE_TTY"] = "100%"
    };
}
