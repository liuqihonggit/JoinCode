namespace Tools.Handlers;

/// <summary>
/// 进程干预工具处理器 — 枚举/结束/启动进程（PRD S-01）
/// </summary>
[McpToolDispatch(ToolCategory.DesktopControl)]
public class ProcessToolHandlers
{
    private readonly ILogger<ProcessToolHandlers>? _logger;

    public ProcessToolHandlers(ILogger<ProcessToolHandlers>? logger = null) => _logger = logger;

    /// <summary>枚举运行中进程（S-01）</summary>
    [McpTool("list_processes", "枚举运行中进程,可按名称过滤,返回PID/名称/主窗口标题", "desktop")]
    public Task<ToolResult> ListProcessesAsync(
        [McpToolParameter("进程名过滤（不传则返回全部）", Required = false)] string? nameFilter = null,
        [McpToolParameter("最大返回数量", Required = false)] int maxCount = 50,
        CancellationToken ct = default)
    {
        var processes = System.Diagnostics.Process.GetProcesses();
        var filtered = processes
            .Where(p => string.IsNullOrEmpty(nameFilter) || p.ProcessName.Contains(nameFilter, StringComparison.OrdinalIgnoreCase))
            .Take(maxCount)
            .Select(p => new { p.Id, p.ProcessName, Title = TryGetMainWindowTitle(p) })
            .ToList();

        var sb = new StringBuilder(256);
        sb.AppendLine($"共 {filtered.Count} 个进程" + (nameFilter is not null ? $"（过滤: {nameFilter}）" : string.Empty) + ":");
        foreach (var p in filtered)
        {
            sb.AppendLine($"  PID={p.Id} {p.ProcessName}" +
                (!string.IsNullOrEmpty(p.Title) ? $" [{p.Title}]" : string.Empty));
        }

        foreach (var p in processes) p.Dispose();

        return Task.FromResult(ToolResultBuilder.Success().WithText(sb.ToString()).Build());
    }

    /// <summary>结束进程（S-01）</summary>
    [McpTool("kill_process", "按PID或名称结束进程", "desktop")]
    public async Task<ToolResult> KillProcessAsync(
        [McpToolParameter("进程ID（优先使用）", Required = false)] int? pid = null,
        [McpToolParameter("进程名称（pid 未传时使用）", Required = false)] string? name = null,
        [McpToolParameter("是否强制终止", Required = false)] bool force = true,
        CancellationToken ct = default)
    {
        if (pid is null && string.IsNullOrEmpty(name))
            return ToolResultBuilder.Error().WithText("必须提供 pid 或 name").Build();

        var targets = pid is not null
            ? new[] { System.Diagnostics.Process.GetProcessById(pid.Value) }
            : System.Diagnostics.Process.GetProcessesByName(name!);

        if (targets.Length == 0)
            return ToolResultBuilder.Error().WithText($"未找到进程: {name}").Build();

        var sb = new StringBuilder(128);
        foreach (var p in targets)
        {
            var procName = "unknown";
            try { procName = p.ProcessName; }
            catch (Exception ex) { _logger?.LogWarning(ex, "获取进程名失败 PID={Pid}", p.Id); }
            try
            {
                if (force) p.Kill(); else p.CloseMainWindow();
                p.WaitForExit(3000);
                sb.AppendLine($"结束进程 PID={p.Id} {procName}: {(p.HasExited ? "成功" : "超时")}");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"结束进程 PID={p.Id} {procName} 失败: {ex.Message}");
            }
            finally
            {
                p.Dispose();
            }
        }

        return ToolResultBuilder.Success().WithText(sb.ToString()).Build();
    }

    /// <summary>启动进程（S-01）</summary>
    [McpTool("start_process", "启动新进程,返回PID", "desktop")]
    public async Task<ToolResult> StartProcessAsync(
        [McpToolParameter("可执行文件路径或名称", Required = true)] string fileName,
        [McpToolParameter("命令行参数", Required = false)] string? arguments = null,
        [McpToolParameter("工作目录", Required = false)] string? workingDir = null,
        CancellationToken ct = default)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(fileName)
        {
            UseShellExecute = true
        };

        if (!string.IsNullOrEmpty(arguments))
            psi.Arguments = arguments;
        if (!string.IsNullOrEmpty(workingDir))
            psi.WorkingDirectory = workingDir;

        var process = System.Diagnostics.Process.Start(psi);
        if (process is null)
            return ToolResultBuilder.Error().WithText($"启动失败: {fileName}").Build();

        await Task.Delay(500, ct).ConfigureAwait(false);

        return ToolResultBuilder.Success()
            .WithText($"已启动: {fileName} PID={process.Id}")
            .Build();
    }

    private static string TryGetMainWindowTitle(System.Diagnostics.Process p)
    {
        try { return p.MainWindowTitle; }
        catch { return string.Empty; }
    }
}
