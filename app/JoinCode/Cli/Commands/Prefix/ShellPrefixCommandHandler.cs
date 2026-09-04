namespace JoinCode.Cli.Commands.Prefix;

/// <summary>
/// ! 前缀命令处理器 — 执行 shell 命令，输出注入 AI 上下文，触发流式响应。
/// 对齐 PI: `!command` runs and sends output to the model.
/// </summary>
[PrefixCommand(Prefix = "!", Description = "执行 shell 命令，输出发送给 AI 分析", TriggersAi = true)]
public sealed class ShellPrefixCommandHandler : IPrefixCommandHandler
{
    private const int DefaultTimeoutMs = 30_000;
    private const int MaxOutputChars = 50_000;

    /// <inheritdoc/>
    public string Prefix => "!";

    /// <inheritdoc/>
    public bool TriggersAi => true;

    /// <inheritdoc/>
    public async Task<PrefixCommandResult> ExecuteAsync(
        string command,
        PrefixCommandContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command))
            return PrefixCommandResult.NotHandled;

        var output = await ShellExecutor.RunAsync(
            command, context.WorkingDirectory, DefaultTimeoutMs, MaxOutputChars, cancellationToken).ConfigureAwait(false);

        var injected = $"$ {command}\n{output}\n\n（以上为 `!{command}` 执行结果，请分析）";
        return new PrefixCommandResult(true, injected, ShouldInjectToAi: true);
    }
}
