namespace JoinCode.Cli.Output;

/// <summary>
/// CLI 输出契约 — stdout/stderr 严格分离的结构化写入器
/// 对齐架构指南：stdout 只输出结构化数据，stderr 只输出日志和提示
/// </summary>
public sealed class CliOutputContract
{
    private readonly bool _jsonMode;
    private readonly CliOutputJsonContext _jsonContext;

    public CliOutputContract(bool jsonMode, CliOutputJsonContext jsonContext)
    {
        _jsonMode = jsonMode;
        _jsonContext = jsonContext;
    }

    /// <summary>
    /// 写入成功数据到 stdout
    /// JSON 模式: 输出 {ok:true, data:..., meta:...}
    /// 文本模式: 直接输出 data 的 ToString()
    /// </summary>
    public void WriteData(object? data, CliOutputMeta? meta = null)
    {
        if (_jsonMode)
        {
            var envelope = CliOutputEnvelope.Success(data, meta);
            var json = System.Text.Json.JsonSerializer.Serialize(envelope, _jsonContext.CliOutputEnvelope);
            Console.WriteLine(json);
        }
        else
        {
            if (data is not null)
                TerminalHelper.WriteLine(data.ToString());
        }
    }

    /// <summary>
    /// 写入结构化错误到 stderr
    /// JSON 模式: stderr 输出 {ok:false, error:{code,message,hint,retryable}}
    /// 文本模式: stderr 输出人类可读错误（带颜色）
    /// </summary>
    public void WriteError(CliStructuredError error)
    {
        if (_jsonMode)
        {
            var envelope = CliOutputEnvelope.Fail(error);
            var json = System.Text.Json.JsonSerializer.Serialize(envelope, _jsonContext.CliOutputEnvelope);
            TerminalHelper.WriteError(json);
        }
        else
        {
            var prev = TerminalHelper.ForegroundColor;
            try
            {
                TerminalHelper.ForegroundColor = ConsoleColor.Red;
                TerminalHelper.WriteErrorRaw($"  ✖ [{error.Code}] {error.Message}");
                TerminalHelper.WriteError();
            }
            finally
            {
                TerminalHelper.ForegroundColor = prev;
            }

            if (!string.IsNullOrEmpty(error.Hint))
            {
                TerminalHelper.ForegroundColor = ConsoleColor.Cyan;
                TerminalHelper.WriteErrorRaw($"  💡 {error.Hint}");
                TerminalHelper.WriteError();
                TerminalHelper.ForegroundColor = prev;
            }
        }
    }

    /// <summary>
    /// 写入日志/提示到 stderr（不影响 stdout 数据流）
    /// </summary>
    public void WriteLog(string message)
    {
        TerminalHelper.WriteError(message);
    }
}
