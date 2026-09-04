namespace JoinCode.Cli.Commands.Prefix;

/// <summary>
/// !! 前缀命令处理器 — 智能识别 target 类型，不触发 AI。
/// 对齐 PI: `!!command` runs without sending output to the model.
/// 智能识别：URL→浏览器、文件→默认程序、目录→资源管理器、其他→shell 静默执行。
/// </summary>
[PrefixCommand(Prefix = "!!", Description = "静默执行/打开，不触发 AI", TriggersAi = false)]
public sealed class SilentShellPrefixCommandHandler : IPrefixCommandHandler
{
    private const int DefaultTimeoutMs = 30_000;
    private const int MaxOutputChars = 50_000;

    private static readonly FrozenSet<string> UrlSchemes = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase, "http", "https");

    /// <inheritdoc/>
    public string Prefix => "!!";

    /// <inheritdoc/>
    public bool TriggersAi => false;

    /// <inheritdoc/>
    public async Task<PrefixCommandResult> ExecuteAsync(
        string command,
        PrefixCommandContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command))
            return PrefixCommandResult.NotHandled;

        var target = command.Trim();

        if (TryHandleUrl(target, out var urlResult))
            return urlResult;

        if (TryHandleFile(target, out var fileResult))
            return fileResult;

        if (TryHandleDirectory(target, out var dirResult))
            return dirResult;

        var output = await ShellExecutor.RunAsync(
            target, context.WorkingDirectory, DefaultTimeoutMs, MaxOutputChars, cancellationToken).ConfigureAwait(false);
        return new PrefixCommandResult(true, $"$ {target}\n{output}", ShouldInjectToAi: false);
    }

    /// <summary>URL → 系统默认浏览器打开</summary>
    private static bool TryHandleUrl(string target, out PrefixCommandResult result)
    {
        result = default!;
        var schemeEnd = target.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd <= 0)
            return false;

        var scheme = target[..schemeEnd];
        if (!UrlSchemes.Contains(scheme))
            return false;

        OpenWithDefaultProgram(target);
        result = new PrefixCommandResult(true, $"已用浏览器打开: {target}", ShouldInjectToAi: false);
        return true;
    }

    /// <summary>文件 → 系统默认程序打开</summary>
    private static bool TryHandleFile(string target, out PrefixCommandResult result)
    {
        result = default!;
        if (!File.Exists(target))
            return false;

        OpenWithDefaultProgram(target);
        result = new PrefixCommandResult(true, $"已打开文件: {target}", ShouldInjectToAi: false);
        return true;
    }

    /// <summary>目录 → 文件管理器打开（Windows: explorer / Linux: xdg-open / macOS: open）</summary>
    private static bool TryHandleDirectory(string target, out PrefixCommandResult result)
    {
        result = default!;
        if (!Directory.Exists(target))
            return false;

        OpenDirectory(target);
        result = new PrefixCommandResult(true, $"已打开目录: {target}", ShouldInjectToAi: false);
        return true;
    }

    private static void OpenDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true,
            });
            return;
        }

        var opener = OperatingSystem.IsMacOS() ? "open" : "xdg-open";
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = opener,
            Arguments = $"\"{path}\"",
            UseShellExecute = false,
        });
    }

    private static void OpenWithDefaultProgram(string path)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
        });
    }
}
