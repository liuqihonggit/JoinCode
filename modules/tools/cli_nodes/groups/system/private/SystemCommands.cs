using System.Collections.Frozen;
using System.Diagnostics;
using System.Globalization;
using JoinCode.Abstractions.Tools;

namespace JoinCode.CliNode.System;

/// <summary>
/// system 分组的本地命令表。
/// <para>本地实现 26 个「无宿主状态」或「文件即真相源」的工具: sleep / sleep_until / get_process_info、
/// config 读、错误诊断、permission 读写、model_search / policy_list、通知交互、brief_status。</para>
/// <para>其余（config_set / agent_* / analytics_* / memory_* / cron 写 …）读写宿主持有的进程内状态或启动期快照，
/// 零依赖 exe 复制一份只会分叉出第二个真相源，因此继续转发宿主。</para>
/// </summary>
internal static partial class SystemCommands {
    private const int MaxSleepSeconds = 1800;

    private static readonly FrozenDictionary<string, CliNodeHandler> Handlers =
        new Dictionary<string, CliNodeHandler>(StringComparer.Ordinal) {
            ["sleep"] = SleepAsync,
            ["sleep_until"] = SleepUntilAsync,
            ["get_process_info"] = ProcessInfoAsync,
            // 纯 BCL 配置/诊断/权限读/通知交互工具
            ["config_get"] = ConfigGetAsync,
            ["config_list"] = ConfigListAsync,
            ["diagnose_error"] = DiagnoseErrorAsync,
            ["fix_shell_error"] = FixShellErrorAsync,
            ["fix_file_error"] = FixFileErrorAsync,
            ["fix_merge_conflict"] = FixMergeConflictAsync,
            ["permission_list_rules"] = PermissionListRulesAsync,
            ["permission_add_rule"] = PermissionAddRuleAsync,
            ["permission_remove_rule"] = PermissionRemoveRuleAsync,
            ["permission_clear_rules"] = PermissionClearRulesAsync,
            ["permission_check_tool"] = PermissionCheckToolAsync,
            ["permission_check_path"] = PermissionCheckPathAsync,
            ["permission_get_agent_rule"] = PermissionGetAgentRuleAsync,
            ["model_search"] = ModelSearchAsync,
            ["policy_list"] = PolicyListAsync,
            ["push_notification"] = PushNotificationAsync,
            ["remote_trigger"] = RemoteTriggerAsync,
            ["shake_window"] = ShakeWindowAsync,
            ["flash_taskbar"] = FlashTaskbarAsync,
            ["send_user_message"] = SendUserMessageAsync,
            ["brief_status"] = BriefStatusAsync
        }.ToFrozenDictionary(StringComparer.Ordinal);

    public static CliNodeHandler? Find(string toolId) => Handlers.TryGetValue(toolId, out var handler) ? handler : null;

    public static IEnumerable<string> LocalToolIds => Handlers.Keys.OrderBy(id => id, StringComparer.Ordinal);

    private static async Task<int> SleepAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var seconds = GetInt(o, "duration_seconds", -1);
        if (seconds < 0 || seconds > MaxSleepSeconds)
            throw new ArgumentException($"duration_seconds 必须在 0..{MaxSleepSeconds} 之间，当前 {seconds}。");
        var reason = Nullable(o, "reason");
        var tick = GetInt(o, "tick_interval_seconds", 0);
        if (tick < 0 || tick >= Math.Max(1, seconds))
            throw new ArgumentException($"tick_interval_seconds 必须小于 duration_seconds，当前 {tick}。");

        var total = TimeSpan.FromSeconds(seconds);
        var suffix = string.IsNullOrWhiteSpace(reason) ? string.Empty : $"，原因：{reason}";
        Console.Out.WriteLine($"开始等待 {seconds} 秒{suffix}");

        var elapsed = Stopwatch.StartNew();
        if (tick == 0) {
            await Task.Delay(total, ct).ConfigureAwait(false);
        } else {
            var waited = 0;
            while (waited < seconds) {
                var step = Math.Min(tick, seconds - waited);
                await Task.Delay(TimeSpan.FromSeconds(step), ct).ConfigureAwait(false);
                waited += step;
                Console.Out.WriteLine($"[等待中] 已过 {waited}/{seconds} 秒");
            }
        }
        elapsed.Stop();
        Console.Out.WriteLine($"等待结束，实际耗时 {elapsed.Elapsed.TotalSeconds:F1} 秒。");
        return 0;
    }

    private static async Task<int> SleepUntilAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var raw = Required(o, "target_time", "sleep_until");
        var offsetHours = GetInt(o, "timezone_offset_hours", int.MinValue);
        var offset = offsetHours == int.MinValue
            ? TimeZoneInfo.Local.GetUtcOffset(DateTimeOffset.UtcNow)
            : TimeSpan.FromHours(offsetHours);

        var target = ParseTarget(raw, offset);
        var now = DateTimeOffset.UtcNow;
        while (target <= now) target = target.AddDays(1);

        var wait = target - now;
        if (wait > TimeSpan.FromHours(24))
            throw new ArgumentException($"目标时间 {raw} 距现在超过 24 小时，sleep_until 只做单次等待。");

        Console.Out.WriteLine($"等待到 {target:yyyy-MM-dd HH:mm:ss}（UTC{FormatOffset(target.Offset)}），剩余 {wait.TotalMinutes:F1} 分钟");
        await Task.Delay(wait, ct).ConfigureAwait(false);
        // 用与目标同一时区打印，否则 UTC 与本地的时差会让人误以为提前/超时到达。
        Console.Out.WriteLine($"已到达目标时间 {DateTimeOffset.UtcNow.ToOffset(target.Offset):yyyy-MM-dd HH:mm:ss}。");
        return 0;
    }

    private static Task<int> ProcessInfoAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        Console.Out.WriteLine($"电脑名: {Environment.MachineName}");
        Console.Out.WriteLine($"PID: {Environment.ProcessId}");
        return Task.FromResult(0);
    }

    /// <summary>支持 HH:mm 与 yyyy-MM-dd HH:mm:ss；已过的时间点顺延到下一次出现（并打印实际等待目标）。</summary>
    private static DateTimeOffset ParseTarget(string raw, TimeSpan offset) {
        var local = DateTimeOffset.UtcNow.ToOffset(offset);
        if (TimeOnly.TryParse(raw, CultureInfo.InvariantCulture, out var timeOnly)) {
            return new DateTimeOffset(local.Year, local.Month, local.Day, timeOnly.Hour, timeOnly.Minute, timeOnly.Second, offset);
        }
        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) {
            return parsed.ToOffset(offset);
        }
        throw new ArgumentException($"target_time 格式无法识别: '{raw}'。正确写法: --target_time 14:30 或 --target_time \"2026-11-17 09:00:00\"。");
    }

    private static string FormatOffset(TimeSpan offset) =>
        (offset < TimeSpan.Zero ? "-" : "+") + offset.ToString(@"hh\:mm", CultureInfo.InvariantCulture);

    private static string Required(IReadOnlyDictionary<string, string> o, string key, string toolId) {
        if (o.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)) return value;
        throw new ArgumentException($"工具 '{toolId}' 缺少必填参数 --{key}。正确写法: {toolId} -h 查看参数。");
    }

    private static string? Nullable(IReadOnlyDictionary<string, string> o, string key) =>
        o.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static int GetInt(IReadOnlyDictionary<string, string> o, string key, int fallback) {
        if (!o.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw)) return fallback;
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)) return value;
        throw new ArgumentException($"参数 --{key} 需要整数，当前值 '{raw}'。");
    }
}
