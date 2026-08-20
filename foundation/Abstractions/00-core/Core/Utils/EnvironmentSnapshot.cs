namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 环境信息快照 — 统一采集 OS/运行时/工具链信息，消除 3 处重复采集
/// <para>
/// 消费方：
/// - EnvironmentSection（系统提示词注入）
/// - VersionCommand（/version 命令）
/// - DoctorCommand（/doctor 诊断命令）
/// - ToolErrorLogger（工具出错时自动记录环境）
/// </para>
/// </summary>
public sealed record EnvironmentSnapshot
{
    /// <summary>采集时间（UTC）</summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>OS 描述</summary>
    public string OsDescription { get; init; } = RuntimeInformation.OSDescription;

    /// <summary>OS 架构</summary>
    public string OsArchitecture { get; init; } = RuntimeInformation.OSArchitecture.ToString();

    /// <summary>进程架构</summary>
    public string ProcessArchitecture { get; init; } = RuntimeInformation.ProcessArchitecture.ToString();

    /// <summary>框架描述</summary>
    public string FrameworkDescription { get; init; } = RuntimeInformation.FrameworkDescription;

    /// <summary>运行时版本</summary>
    public string RuntimeVersion { get; init; } = Environment.Version.ToString();

    /// <summary>工作目录</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>是否 Git 仓库</summary>
    public bool IsGitRepo { get; init; }

    /// <summary>控制台编码</summary>
    public string? ConsoleEncoding { get; init; }

    /// <summary>开发工具及版本（key=工具名, value=版本或null=已安装但版本未知）</summary>
    public FrozenDictionary<string, string?> DevTools { get; init; } = FrozenDictionary<string, string?>.Empty;

    /// <summary>
    /// 捕获当前环境快照（不含开发工具检测，同步快速采集）
    /// </summary>
    public static EnvironmentSnapshot CaptureQuick(
        IFileSystem? fs = null,
        string? workingDirectory = null,
        bool detectGit = true)
    {
        var cwd = workingDirectory ?? fs?.GetCurrentDirectory() ?? Environment.CurrentDirectory;
        var isGitRepo = detectGit && fs is not null && fs.DirectoryExists(fs.CombinePath(cwd, ".git"));
        var consoleEncoding = System.Console.OutputEncoding?.WebName;

        return new EnvironmentSnapshot
        {
            WorkingDirectory = cwd,
            IsGitRepo = isGitRepo,
            ConsoleEncoding = consoleEncoding
        };
    }

    /// <summary>
    /// 捕获完整环境快照（含开发工具检测，异步）
    /// </summary>
    public static async Task<EnvironmentSnapshot> CaptureFullAsync(
        IProcessService? processService = null,
        IFileSystem? fs = null,
        string? workingDirectory = null,
        CancellationToken ct = default)
    {
        var snapshot = CaptureQuick(fs, workingDirectory);
        var devTools = await DetectDevToolsAsync(processService, ct).ConfigureAwait(false);

        return snapshot with { DevTools = devTools };
    }

    /// <summary>
    /// 检测开发工具及版本 — 统一实现，消除 EnvironmentSection/DoctorCommand 两处重复
    /// <para>优先使用 IProcessService（走安全检查），无则回退到裸 Process.Start</para>
    /// </summary>
    public static async Task<FrozenDictionary<string, string?>> DetectDevToolsAsync(
        IProcessService? processService = null,
        CancellationToken ct = default)
    {
        var tools = new[] { "node", "python", "go", "rustc", "java", "dotnet", "php", "ruby" };
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var tool in tools)
        {
            var version = await TryDetectToolVersionAsync(tool, processService, ct).ConfigureAwait(false);
            if (version is not null)
            {
                result[tool] = version;
            }
        }

        // PowerShell 特殊处理（Windows 内置）
        if (OperatingSystem.IsWindows())
        {
            result["powershell"] = Environment.Version.ToString();
        }

        return result.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 检测单个工具版本 — 优先用 IProcessService，无则回退到裸 Process.Start
    /// </summary>
    private static async Task<string?> TryDetectToolVersionAsync(
        string toolName,
        IProcessService? processService,
        CancellationToken ct)
    {
        var versionFlag = toolName is "java" ? "-version" : "--version";

        try
        {
            if (processService is not null)
            {
                // 走 IProcessService（安全检查 + 编码处理）
                var result = await processService.ExecuteAsync(new ProcessOptions
                {
                    FileName = toolName,
                    Arguments = versionFlag,
                    TimeoutMs = 5000,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }, ct).ConfigureAwait(false);

                if (result.Success || result.ExitCode == 0)
                {
                    return ExtractVersion(result.StandardOutput) ?? ExtractVersion(result.StandardError);
                }
            }
            else
            {
                // 回退：裸 Process.Start（用于无法注入 IProcessService 的场景）
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = toolName,
                    Arguments = versionFlag,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(psi);
                if (process is null) return null;

                var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
                var stderrTask = process.StandardError.ReadToEndAsync(ct);
                await process.WaitForExitAsync(ct).ConfigureAwait(false);
                var stdout = await stdoutTask.ConfigureAwait(false);
                var stderr = await stderrTask.ConfigureAwait(false);
                return ExtractVersion(stdout) ?? ExtractVersion(stderr);
            }
        }
        catch (OperationCanceledException)
        {
            // 取消异常，静默处理
        }
        catch (Exception ex)
        {
            // 工具不存在或执行失败，记录到 stderr
            Console.Error.WriteLine($"[EnvironmentSnapshot] 检测工具 {toolName} 失败: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// 从命令输出中提取版本号
    /// </summary>
    private static string? ExtractVersion(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) return null;

        var firstLine = output.AsSpan().Trim();
        var newlineIdx = firstLine.IndexOf('\n');
        if (newlineIdx > 0) firstLine = firstLine[..newlineIdx];

        var trimmed = firstLine.Trim().ToString();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    /// <summary>
    /// 格式化为可读字符串（用于日志/诊断输出）
    /// </summary>
    public string FormatReadable()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"  时间: {Timestamp:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"  OS: {OsDescription}");
        sb.AppendLine($"  架构: {ProcessArchitecture}");
        sb.AppendLine($"  运行时: {FrameworkDescription} (.NET {RuntimeVersion})");

        if (WorkingDirectory is not null)
        {
            sb.AppendLine($"  工作目录: {WorkingDirectory}");
            sb.AppendLine($"  Git仓库: {(IsGitRepo ? "是" : "否")}");
        }

        if (ConsoleEncoding is not null)
        {
            var isUtf8 = ConsoleEncoding.Equals("utf-8", StringComparison.OrdinalIgnoreCase);
            sb.AppendLine($"  控制台编码: {ConsoleEncoding}{(isUtf8 ? "" : " (非UTF-8)")}");
        }

        if (DevTools.Count > 0)
        {
            var tools = DevTools.Select(static kv => kv.Value is null ? kv.Key : $"{kv.Key} {kv.Value}");
            sb.AppendLine($"  开发工具: {string.Join(", ", tools)}");
        }

        return sb.ToString();
    }
}
