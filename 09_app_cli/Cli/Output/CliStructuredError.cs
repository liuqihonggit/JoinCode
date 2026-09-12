namespace JoinCode.Cli.Output;

/// <summary>
/// CLI 结构化错误模型 — 对齐架构指南4字段规范
/// </summary>
public sealed class CliStructuredError
{
    /// <summary>机器可读错误码（如 AUTH_API_KEY_MISSING、CONFIG_INVALID_MODEL）</summary>
    public string Code { get; init; }

    /// <summary>人类可读错误描述</summary>
    public string Message { get; init; }

    /// <summary>修复建议（可选，如 "请运行 jcc --doctor 检查配置"）</summary>
    public string? Hint { get; init; }

    /// <summary>是否可重试（如网络超时=true，认证失败=false）</summary>
    public bool Retryable { get; init; }

    public CliStructuredError(string code, string message, string? hint = null, bool retryable = false)
    {
        Code = code ?? throw new ArgumentNullException(nameof(code));
        Message = message ?? throw new ArgumentNullException(nameof(message));
        Hint = hint;
        Retryable = retryable;
    }

    /// <summary>
    /// 渲染为 Rust 编译器风格错误信息 — 完整命令行 + ^ 位置指示线 + hint 修复建议
    /// <para>示例:</para>
    /// <para>error: 未知选项</para>
    /// <para>  |</para>
    /// <para>  | mcp_call tool --bad-flag key=value</para>
    /// <para>  |          ^^^^^^^^^^^^^^ 未知选项</para>
    /// <para>  |</para>
    /// <para>hint: 可用选项见 jcc --help</para>
    /// </summary>
    /// <param name="args">原始参数数组</param>
    /// <param name="errorArgIndex">错误参数在 args 中的索引</param>
    public string ToRustStyleString(string[] args, int errorArgIndex)
    {
        ArgumentNullException.ThrowIfNull(args);
        var sb = new StringBuilder();
        sb.AppendLine($"error: {Message}");
        sb.AppendLine("  |");

        var cmdLine = string.Join(" ", args);
        var pos = 0;
        for (var i = 0; i < errorArgIndex; i++)
            pos += args[i].Length + 1;

        sb.AppendLine($"  | {cmdLine}");
        var arrow = new string(' ', pos) + new string('^', args[errorArgIndex].Length);
        sb.AppendLine($"  | {arrow} {Message}");
        sb.AppendLine("  |");
        if (!string.IsNullOrEmpty(Hint))
            sb.AppendLine($"hint: {Hint}");
        return sb.ToString();
    }

    /// <summary>
    /// 渲染为 Rust 编译器风格错误信息 — 单 token 级别位置指示
    /// <para>示例:</para>
    /// <para>error: 参数格式错误</para>
    /// <para>  |</para>
    /// <para>  | pr_number=</para>
    /// <para>  | ^^^^^^^^^^ '=' 后面不能为空</para>
    /// <para>  |</para>
    /// <para>hint: 使用 key=value 传递工具参数，如 pr_number=201</para>
    /// </summary>
    /// <param name="token">格式错误的 token</param>
    public string ToRustStyleString(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        var sb = new StringBuilder();
        sb.AppendLine($"error: {Message}");
        sb.AppendLine("  |");
        sb.AppendLine($"  | {token}");
        var arrow = new string('^', token.Length);
        sb.AppendLine($"  | {arrow} {Message}");
        sb.AppendLine("  |");
        if (!string.IsNullOrEmpty(Hint))
            sb.AppendLine($"hint: {Hint}");
        return sb.ToString();
    }
}
