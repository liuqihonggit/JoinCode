namespace Tools.Shell;

/// <summary>
/// Shell 超时关键字提取器 — 解析脚本命令中的等待关键字（sleep/Start-Sleep/timeout 等）
/// 提取最大等待时间（秒），用于动态调整超时上限，规避默认超时终止
/// 支持范围: PowerShell + Bash + cmd.exe + Python/C# 内嵌脚本
/// </summary>
public static partial class ShellTimeoutKeywordExtractor
{
    /// <summary>
    /// 从命令文本中提取最大等待时间（秒）。返回 null 表示无等待关键字。
    /// 取所有匹配中的最大值（脚本可能含多个 sleep）。
    /// </summary>
    public static int? ExtractMaxWaitSeconds(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return null;

        var maxSeconds = 0;
        var found = false;

        Accumulate(PowerShellStartSleepSecondsRegex().Matches(command), ScaleSeconds, ref maxSeconds, ref found);
        Accumulate(PowerShellStartSleepMillisecondsRegex().Matches(command), ScaleMilliseconds, ref maxSeconds, ref found);
        Accumulate(PowerShellStartSleepSecondsShortRegex().Matches(command), ScaleSeconds, ref maxSeconds, ref found);
        Accumulate(PowerShellStartSleepMillisecondsShortRegex().Matches(command), ScaleMilliseconds, ref maxSeconds, ref found);
        Accumulate(PowerShellStartSleepPositionalRegex().Matches(command), ScaleSeconds, ref maxSeconds, ref found);
        Accumulate(BashSleepRegex().Matches(command), ScaleBashSleep, ref maxSeconds, ref found);
        Accumulate(CmdTimeoutRegex().Matches(command), ScaleSeconds, ref maxSeconds, ref found);
        Accumulate(CmdPingDelayRegex().Matches(command), ScaleSeconds, ref maxSeconds, ref found);
        Accumulate(PythonSleepRegex().Matches(command), ScaleSeconds, ref maxSeconds, ref found);
        Accumulate(CSharpThreadSleepRegex().Matches(command), ScaleMilliseconds, ref maxSeconds, ref found);

        return found ? maxSeconds : null;
    }

    private static void Accumulate(
        MatchCollection matches,
        Func<Match, int> scaler,
        ref int maxSeconds,
        ref bool found)
    {
        for (var i = 0; i < matches.Count; i++)
        {
            var seconds = scaler(matches[i]);
            if (seconds <= 0)
                continue;
            found = true;
            if (seconds > maxSeconds)
                maxSeconds = seconds;
        }
    }

    private static int ScaleSeconds(Match m)
    {
        return double.TryParse(m.Groups[1].ValueSpan, CultureInfo.InvariantCulture, out var sec) && sec > 0
            ? (int)Math.Ceiling(sec)
            : 0;
    }

    private static int ScaleMilliseconds(Match m)
    {
        return double.TryParse(m.Groups[1].ValueSpan, CultureInfo.InvariantCulture, out var ms) && ms > 0
            ? (int)Math.Ceiling(ms / 1000.0)
            : 0;
    }

    private static int ScaleBashSleep(Match m)
    {
        if (!double.TryParse(m.Groups[1].ValueSpan, CultureInfo.InvariantCulture, out var val) || val <= 0)
            return 0;
        var seconds = m.Groups[2].Value switch
        {
            "s" => val,
            "m" => val * 60,
            "h" => val * 3600,
            "d" => val * 86400,
            _ => val
        };
        return (int)Math.Ceiling(seconds);
    }

    [GeneratedRegex(@"Start-Sleep\s+-Seconds\s+([\d.]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PowerShellStartSleepSecondsRegex();

    [GeneratedRegex(@"Start-Sleep\s+-Milliseconds\s+([\d.]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PowerShellStartSleepMillisecondsRegex();

    [GeneratedRegex(@"Start-Sleep\s+-s\s+([\d.]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PowerShellStartSleepSecondsShortRegex();

    [GeneratedRegex(@"Start-Sleep\s+-ms\s+([\d.]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PowerShellStartSleepMillisecondsShortRegex();

    [GeneratedRegex(@"Start-Sleep\s+(?!-)([\d.]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PowerShellStartSleepPositionalRegex();

    [GeneratedRegex(@"\bsleep\s+([\d.]+)([smhd]?)", RegexOptions.CultureInvariant)]
    private static partial Regex BashSleepRegex();

    [GeneratedRegex(@"\btimeout\s+/t\s+(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CmdTimeoutRegex();

    [GeneratedRegex(@"\bping\s+-n\s+(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CmdPingDelayRegex();

    [GeneratedRegex(@"time\.sleep\(([\d.]+)\)", RegexOptions.CultureInvariant)]
    private static partial Regex PythonSleepRegex();

    [GeneratedRegex(@"Thread\.Sleep\((\d+)\)", RegexOptions.CultureInvariant)]
    private static partial Regex CSharpThreadSleepRegex();
}
