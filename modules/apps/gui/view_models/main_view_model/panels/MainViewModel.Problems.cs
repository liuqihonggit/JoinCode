namespace JoinCode.Gui.ViewModels;

/// <summary>
/// MainViewModel 问题面板 partial — 扫描项目编译错误/警告，显示在底部面板问题 tab。
/// 运行 dotnet build 解析输出中的 error/warning 行。
/// </summary>
public sealed partial class MainViewModel {
    /// <summary>问题列表（编译错误/警告）</summary>
    [ObservableProperty]
    private IReadOnlyList<ProblemItemVm> _panelProblems = [];

    /// <summary>是否正在扫描问题</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanProblemsCommand))]
    private bool _isProblemsScanning;

    /// <summary>错误数量</summary>
    public int ProblemErrorCount => PanelProblems.Count(static p => p.Severity == ProblemSeverity.Error);

    /// <summary>警告数量</summary>
    public int ProblemWarningCount => PanelProblems.Count(static p => p.Severity == ProblemSeverity.Warning);

    /// <summary>是否可以扫描问题</summary>
    private bool CanScanProblems => !IsProblemsScanning;

    /// <summary>扫描项目编译问题 — 运行 dotnet build 解析输出</summary>
    [RelayCommand(CanExecute = nameof(CanScanProblems))]
    private async Task ScanProblemsAsync() {
        var projectDir = !string.IsNullOrEmpty(FileTreeRootPath) ? FileTreeRootPath : AppContext.BaseDirectory;
        if (!System.IO.Directory.Exists(projectDir))
            return;
        IsProblemsScanning = true;
        try {
            var psi = new System.Diagnostics.ProcessStartInfo {
                FileName = "dotnet",
                Arguments = "build --no-restore -v q",
                WorkingDirectory = projectDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            using var process = new System.Diagnostics.Process { StartInfo = psi };
            process.Start();
            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(CancellationToken.None);
            PanelProblems = ParseBuildOutput(stdout + "\n" + stderr);
            OnPropertyChanged(nameof(ProblemErrorCount));
            OnPropertyChanged(nameof(ProblemWarningCount));
        } catch (Exception ex) {
            PanelProblems = [new ProblemItemVm { Severity = ProblemSeverity.Error, Message = ex.Message, File = "", Line = 0, Column = 0 }];
        } finally {
            IsProblemsScanning = false;
        }
    }

    /// <summary>解析 dotnet build 输出，提取 error/warning 行</summary>
    private static IReadOnlyList<ProblemItemVm> ParseBuildOutput(string output) {
        var regex = new System.Text.RegularExpressions.Regex(
            @"^(.+?)\((\d+),(\d+)\):\s+(error|warning)\s+(.+)$",
            System.Text.RegularExpressions.RegexOptions.Multiline);
        return regex.Matches(output)
            .Select(static m => new ProblemItemVm {
                Severity = m.Groups[4].Value == "error" ? ProblemSeverity.Error : ProblemSeverity.Warning,
                Message = m.Groups[5].Value.Trim(),
                File = m.Groups[1].Value.Trim(),
                Line = int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture),
                Column = int.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture),
            })
            .ToList();
    }

    /// <summary>清空问题列表</summary>
    [RelayCommand]
    private void ClearProblems() {
        PanelProblems = [];
        OnPropertyChanged(nameof(ProblemErrorCount));
        OnPropertyChanged(nameof(ProblemWarningCount));
    }
}
