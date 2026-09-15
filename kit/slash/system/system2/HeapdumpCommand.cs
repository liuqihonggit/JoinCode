
namespace JoinCode.ChatCommands;

/// <summary>
/// /heapdump 命令 — 生成堆转储用于诊断
/// 输出当前托管内存、GC 集合次数、线程池、进程内存与句柄数等运行时诊断信息
/// </summary>
[ChatCommand(Name = ChatCommandNameEnumConstants.Heapdump, Description = "生成堆转储用于诊断", Usage = "/heapdump", Category = ChatCommandCategory.System, IsHidden = true)]
public sealed class HeapdumpCommand : ChatCommandBase
{
    /// <summary>
    /// 执行 /heapdump 命令 — 输出 GC 内存、集合次数、线程池、进程内存等运行时诊断信息
    /// </summary>
    /// <param name="context">命令执行上下文</param>
    /// <returns>命令执行结果（始终为 Continue，表示不中断主对话流）</returns>
    public override Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        TerminalHelper.WriteLine("堆转储 / 运行时诊断:");
        TerminalHelper.NewLine();

        var memory = GC.GetTotalMemory(false);
        var peakMemory = GC.GetTotalMemory(true);
        TerminalHelper.WriteLine($"  当前托管内存: {memory / 1024 / 1024:F1} MB");
        TerminalHelper.WriteLine($"  压缩后内存:   {peakMemory / 1024 / 1024:F1} MB");
        TerminalHelper.WriteLine($"  GC 最大代数:  {GC.MaxGeneration}");
        TerminalHelper.NewLine();

        TerminalHelper.WriteLine("  GC 集合次数:");
        TerminalHelper.WriteLine($"    0 代: {GC.CollectionCount(0)}");
        TerminalHelper.WriteLine($"    1 代: {GC.CollectionCount(1)}");
        TerminalHelper.WriteLine($"    2 代: {GC.CollectionCount(2)}");
        TerminalHelper.NewLine();

        System.Threading.ThreadPool.GetAvailableThreads(out var workerThreads, out var completionPortThreads);
        System.Threading.ThreadPool.GetMaxThreads(out var maxWorkerThreads, out var maxCompletionPortThreads);
        TerminalHelper.WriteLine("  线程池:");
        TerminalHelper.WriteLine($"    工作线程: {workerThreads}/{maxWorkerThreads}");
        TerminalHelper.WriteLine($"    I/O 线程: {completionPortThreads}/{maxCompletionPortThreads}");
        TerminalHelper.NewLine();

        TerminalHelper.WriteLine($"  进程内存: {System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024:F1} MB");
        TerminalHelper.WriteLine($"  句柄数:   {System.Diagnostics.Process.GetCurrentProcess().HandleCount}");

        return Task.FromResult(ChatCommandResult.Continue());
    }
}
