namespace JoinCode.Cli.Output;

/// <summary>
/// Fail Fast 检查器 — 前置检查配置/Token/连接，全部通过才执行核心逻辑
/// 对齐架构指南：Fail Fast 策略，失败立刻返回结构化错误
/// </summary>
public sealed class FailFastChecker
{
    private readonly List<CliStructuredError> _errors = [];
    private readonly List<CliStructuredError> _warnings = [];

    /// <summary>是否通过所有前置检查</summary>
    public bool IsOk => _errors.Count == 0;

    /// <summary>收集到的错误列表</summary>
    public IReadOnlyList<CliStructuredError> Errors => _errors.AsReadOnly();

    /// <summary>收集到的警告列表</summary>
    public IReadOnlyList<CliStructuredError> Warnings => _warnings.AsReadOnly();

    /// <summary>
    /// 检查 API Key 是否存在
    /// </summary>
    public FailFastChecker CheckApiKey(string? apiKey, string? provider = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            _errors.Add(CliErrorCatalog.AuthApiKeyMissing(provider));
        return this;
    }

    /// <summary>
    /// 检查 API 端点是否配置
    /// </summary>
    public FailFastChecker CheckEndpoint(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            _errors.Add(CliErrorCatalog.ConfigInvalidValue("Provider.Endpoint", "(empty)", "有效的 API 端点 URL"));
        return this;
    }

    /// <summary>
    /// 检查模型 ID 是否配置
    /// </summary>
    public FailFastChecker CheckModelId(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
            _warnings.Add(CliErrorCatalog.ConfigModelUnavailable("(未配置)"));
        return this;
    }

    /// <summary>
    /// 检查工作目录是否被信任
    /// </summary>
    public FailFastChecker CheckWorkspaceTrust(bool isTrusted, string path)
    {
        if (!isTrusted)
            _errors.Add(CliErrorCatalog.ConflictWorkspaceNotTrusted(path));
        return this;
    }

    /// <summary>
    /// 输出所有错误和警告 — JSON 模式输出到 stderr，文本模式使用 ErrorConsole
    /// </summary>
    public void Report(CliOutputContract? outputContract = null)
    {
        foreach (var warning in _warnings)
        {
            if (outputContract is not null)
                outputContract.WriteLog($"⚠ {warning.Code}: {warning.Message}");
            else
                App.ErrorConsole.Warning($"{warning.Message} ({warning.Code})");
        }

        foreach (var error in _errors)
        {
            if (outputContract is not null)
                outputContract.WriteError(error);
            else
            {
                var prev = TerminalHelper.ForegroundColor;
                try
                {
                    TerminalHelper.ForegroundColor = ConsoleColor.Red;
                    TerminalHelper.WriteError($"  ✖ [{error.Code}] {error.Message}");
                }
                finally
                {
                    TerminalHelper.ForegroundColor = prev;
                }
                if (!string.IsNullOrEmpty(error.Hint))
                {
                    TerminalHelper.ForegroundColor = ConsoleColor.Cyan;
                    TerminalHelper.WriteError($"  💡 {error.Hint}");
                    TerminalHelper.ForegroundColor = prev;
                }
            }
        }
    }
}
