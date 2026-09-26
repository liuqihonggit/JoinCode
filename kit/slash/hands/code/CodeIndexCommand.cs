namespace JoinCode.ChatCommands;

/// <summary>
/// /code-index 命令 — 显式构建/重建代码索引
/// 用法: /code-index          构建或重建当前工作区的代码索引
/// </summary>
[ChatCommand(Name = ChatCommandNameEnumConstants.CodeIndex, Description = "构建或重建代码索引（符号/调用/依赖图）", Usage = "/code-index", Category = ChatCommandCategory.Code, ExposeToMcp = true)]
public sealed class CodeIndexCommand : ChatCommandBase {
    /// <summary>
    /// 执行 /code-index 命令，显式触发索引重建
    /// </summary>
    /// <param name="context">命令执行上下文</param>
    /// <returns>命令执行结果</returns>
    public override async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context) {
        var indexer = context.Services.GetService<ICodeIndexer>();
        if (indexer is null) {
            TerminalHelper.WriteLine("代码索引服务未注册，无法构建索引");
            return ChatCommandResult.Continue();
        }

        TerminalHelper.WriteLine("正在构建代码索引...");
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try {
            await indexer.RebuildIndexAsync(context.CancellationToken).ConfigureAwait(false);
            sw.Stop();

            var stats = await indexer.GetStatsAsync(context.CancellationToken).ConfigureAwait(false);
            TerminalHelper.WriteLine($"代码索引构建完成 ({sw.ElapsedMilliseconds}ms)");
            TerminalHelper.WriteLine($"  文件数: {stats.FileCount}");
            TerminalHelper.WriteLine($"  符号数: {stats.SymbolCount}");
            TerminalHelper.WriteLine($"  调用边: {stats.CallEdgeCount}");
            TerminalHelper.WriteLine($"  依赖边: {stats.DependencyEdgeCount}");
            TerminalHelper.WriteLine($"  项目数: {stats.ProjectCount}");
        } catch (Exception ex) {
            sw.Stop();
            TerminalHelper.WriteLine($"代码索引构建失败 ({sw.ElapsedMilliseconds}ms): {ex.Message}");
        }

        return ChatCommandResult.Continue();
    }
}
