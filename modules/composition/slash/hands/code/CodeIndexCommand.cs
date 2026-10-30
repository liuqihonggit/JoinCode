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

        // 非交互模式(-p)同步执行（进程执行完即退出，后台任务来不及完成）
        if (Core.Utils.TestEnvironmentDetector.IsNonInteractive) {
            TerminalHelper.WriteLine("正在构建代码索引...");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try {
                await indexer.RebuildIndexAsync(context.CancellationToken).ConfigureAwait(false);
                sw.Stop();
                var stats = await indexer.GetStatsAsync(context.CancellationToken).ConfigureAwait(false);
                TerminalHelper.WriteLine($"代码索引构建完成 ({sw.ElapsedMilliseconds}ms)");
                TerminalHelper.WriteLine($"  文件数: {stats.FileCount}, 符号数: {stats.SymbolCount}, 调用边: {stats.CallEdgeCount}, 依赖边: {stats.DependencyEdgeCount}, 项目数: {stats.ProjectCount}");
            } catch (Exception ex) {
                sw.Stop();
                TerminalHelper.WriteLine($"代码索引构建失败 ({sw.ElapsedMilliseconds}ms): {ex.Message}");
            }
            return ChatCommandResult.Continue();
        }

        // 交互模式(TUI)后台执行
        var taskId = Guid.NewGuid().ToString("N")[..8];
        TerminalHelper.WriteLine($"正在后台构建代码索引 (任务ID: {taskId})...");
        TerminalHelper.WriteLine("使用 /tasks 查看任务状态，构建完成后会在此通知");

        _ = Task.Run(async () => {
            try {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                await indexer.RebuildIndexAsync(CancellationToken.None).ConfigureAwait(false);
                sw.Stop();
                var stats = await indexer.GetStatsAsync(CancellationToken.None).ConfigureAwait(false);
                TerminalHelper.WriteLine($"[后台任务 {taskId}] 代码索引构建完成 ({sw.ElapsedMilliseconds}ms)");
                TerminalHelper.WriteLine($"  文件数: {stats.FileCount}, 符号数: {stats.SymbolCount}, 调用边: {stats.CallEdgeCount}, 依赖边: {stats.DependencyEdgeCount}, 项目数: {stats.ProjectCount}");
            } catch (Exception ex) {
                TerminalHelper.WriteLine($"[后台任务 {taskId}] 代码索引构建失败: {ex.Message}");
            }
        });

        return ChatCommandResult.Continue();
    }
}
