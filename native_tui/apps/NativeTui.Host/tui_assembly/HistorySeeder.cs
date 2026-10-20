namespace NativeTui.Host;

/// <summary>
/// 输入历史种子加载器 — 复用 ~/.jcc/sessions transcript 存储（TranscriptPersistMiddleware 已自动落盘 TUI 输入，
/// 无需第二套 history 存储）：最近同项目会话的 user 输入（排除当前会话防双份），聚合为旧→新顺序，
/// 取末尾 HistoryRing.Capacity 条注入 shell。加载失败静默降级（历史种子不阻塞 TUI 启动）。
/// </summary>
public static class HistorySeeder
{
    /// <summary>扫描会话数（项目过滤前取样的窗口）。</summary>
    private const int ScanSessions = 15;

    /// <summary>参与种子的同项目会话数上限。</summary>
    private const int MaxProjectSessions = 5;

    /// <summary>加载历史种子并注入 shell — transcript 服务未注册/加载失败返回（无历史降级）。</summary>
    public static async Task SeedAsync(ReplShell shell, IServiceProvider services, string excludeSessionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var transcript = services.GetService<ITranscriptService>();
            if (transcript is null)
            {
                return;
            }

            var projectRoot = Environment.CurrentDirectory;
            var sessionsNewFirst = new List<List<string>>();
            var summaries = await transcript.ListTranscriptsAsync(ScanSessions, cancellationToken).ConfigureAwait(false);
            foreach (var summary in summaries)
            {
                if (sessionsNewFirst.Count >= MaxProjectSessions)
                {
                    break;
                }

                if (summary.SessionId == excludeSessionId)
                {
                    continue;
                }

                var info = await transcript.GetSessionInfoAsync(summary.SessionId, cancellationToken).ConfigureAwait(false);
                if (info?.ProjectPath != projectRoot)
                {
                    continue;
                }

                var entries = await transcript.LoadTranscriptAsync(summary.SessionId, cancellationToken).ConfigureAwait(false);
                var userInputs = entries
                    .Where(entry => entry.Role == "user" && entry.Type is null && !entry.IsSidechain && entry.Content.Length > 0)
                    .Select(entry => entry.Content)
                    .ToList();
                if (userInputs.Count > 0)
                {
                    sessionsNewFirst.Add(userInputs);
                }
            }

            sessionsNewFirst.Reverse();
            var chronological = sessionsNewFirst.SelectMany(inputs => inputs).ToList();
            shell.SeedHistory(chronological.Skip(Math.Max(0, chronological.Count - HistoryRing.Capacity)));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"输入历史加载失败（已降级为无历史）: {ex.Message}");
        }
    }
}
