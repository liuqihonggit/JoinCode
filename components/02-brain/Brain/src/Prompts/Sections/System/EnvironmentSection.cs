namespace Core.Prompts.Sections;

/// <summary>
/// 环境信息部分 - 动态获取当前开发环境信息
/// </summary>
[PromptSection(Name = "environment", Order = 70, IsDynamic = true)]
public static class EnvironmentSection {
    public static string? GetContent() {
        var fs = PromptConfigSnapshot.Current.FileSystem;
        if (fs is null) return null;

        var additionalInfo = PromptConfigSnapshot.Current.AdditionalEnvInfo;

        var cwd = fs.GetCurrentDirectory();
        var isGitRepo = fs.DirectoryExists(fs.CombinePath(cwd, ".git"));
        var consoleEncoding = System.Console.OutputEncoding?.WebName ?? "unknown";
        var isUtf8 = consoleEncoding.Equals("utf-8", StringComparison.OrdinalIgnoreCase);

        var items = new List<string> {
            $"工作目录: {cwd}",
            $"Git仓库: {(isGitRepo ? "是" : "否")}",
            $"平台: {RuntimeInformation.OSDescription}",
            $"控制台编码: {consoleEncoding}{(isUtf8 ? "" : " (非UTF-8，中文输出可能乱码)")}"
        };

        var devTools = DetectDevTools();
        if (devTools.Count > 0)
        {
            items.Add($"开发工具: {string.Join(", ", devTools)}");
        }

        if (!string.IsNullOrWhiteSpace(additionalInfo)) {
            items.Add(additionalInfo);
        }

        var result = new System.Text.StringBuilder();
        result.AppendLine("# 环境");
        result.AppendLine("您在以下环境中被调用：");
        foreach (var item in items) {
            result.AppendLine($" - {item}");
        }

        return result.ToString().TrimEnd();
    }

    public static SystemPromptSection Create() =>
        SystemPromptSection.Dynamic("environment", GetContent);

    private static List<string> DetectDevTools()
    {
        var tools = new List<string>();
        var detectors = new (string Name, string Cmd, string VersionArg)[]
        {
            ("Node.js", "node", "--version"),
            ("Python", "python", "--version"),
            ("Python3", "python3", "--version"),
            ("Go", "go", "version"),
            ("Rust", "rustc", "--version"),
            ("Java", "java", "-version"),
            ("dotnet", "dotnet", "--version"),
            ("PHP", "php", "--version"),
            ("Ruby", "ruby", "--version"),
        };

        foreach (var (name, cmd, arg) in detectors)
        {
            var version = TryDetectTool(cmd, arg);
            if (version is not null)
                tools.Add($"{name} {version}");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var psVersion = TryDetectTool("pwsh", "-Command \"$PSVersionTable.PSVersion.ToString()\"");
            if (psVersion is not null)
                tools.Add($"PowerShell 7+ {psVersion}");
            else
            {
                var ps5 = TryDetectTool("powershell", "-Command \"$PSVersionTable.PSVersion.ToString()\"");
                if (ps5 is not null)
                    tools.Add($"Windows PowerShell {ps5}");
            }
        }

        return tools;
    }

    private static string? TryDetectTool(string fileName, string args, IProcessService? processService = null)
    {
        try
        {
            if (processService is not null)
            {
                var options = new ProcessOptions
                {
                    FileName = fileName,
                    Arguments = args,
                    TimeoutMs = 3000
                };

                var procResult = processService.ExecuteAsync(options).GetAwaiter().GetResult();
                var procOutput = string.IsNullOrWhiteSpace(procResult.StandardOutput) ? procResult.StandardError : procResult.StandardOutput;
                return string.IsNullOrWhiteSpace(procOutput) ? null : procOutput.Trim();
            }

            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process is null) return null;
            var stdout = process.StandardOutput.ReadToEnd();
            var errOutput = process.StandardError.ReadToEnd();
            process.WaitForExit(3000);
            var output = string.IsNullOrWhiteSpace(stdout) ? errOutput : stdout;
            return string.IsNullOrWhiteSpace(output) ? null : output.Trim();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
