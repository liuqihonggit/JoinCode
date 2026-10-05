namespace JoinCode.Gui.ViewModels;

/// <summary>
/// MainViewModel 集成终端 partial — 在底部面板终端 tab 中执行 shell 命令，
/// 支持命令历史导航、cd 目录切换、cls 清屏等内建命令。
/// </summary>
public sealed partial class MainViewModel {
    /// <summary>终端输出文本（累积所有命令输出）</summary>
    [ObservableProperty]
    private string _terminalOutput = "";

    /// <summary>终端输入框文本</summary>
    [ObservableProperty]
    private string _terminalInput = "";

    /// <summary>终端是否正在执行命令（禁用输入）</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExecuteTerminalCommand))]
    private bool _isTerminalBusy;

    /// <summary>终端工作目录（空字符串表示尚未初始化，首次使用时从 FileTreeRootPath 解析）</summary>
    private string _terminalWorkingDirectory = "";

    /// <summary>终端命令历史</summary>
    private readonly List<string> _terminalHistory = [];

    /// <summary>终端历史游标（-1 表示未在回看中）</summary>
    private int _terminalHistoryIndex = -1;

    /// <summary>终端是否正在程序化填充输入（历史导航时避免重置游标）</summary>
    private bool _isTerminalNavigating;

    /// <summary>解析终端工作目录 — 首次使用时从 FileTreeRootPath 或 AppContext.BaseDirectory 初始化</summary>
    private string ResolveTerminalWorkingDirectory() {
        if (!string.IsNullOrEmpty(_terminalWorkingDirectory))
            return _terminalWorkingDirectory;
        _terminalWorkingDirectory = !string.IsNullOrEmpty(FileTreeRootPath)
            ? FileTreeRootPath
            : AppContext.BaseDirectory;
        return _terminalWorkingDirectory;
    }

    /// <summary>终端提示符（PS 风格，显示当前工作目录）</summary>
    public string TerminalPrompt => $"PS {ResolveTerminalWorkingDirectory()}>";

    /// <summary>终端输入变化时退出历史回看游标（斜杠刷新由 View 层防抖触发）</summary>
    partial void OnTerminalInputChanged(string value) {
        if (!_isTerminalNavigating)
            _terminalHistoryIndex = -1;
    }

    /// <summary>是否可以执行终端命令</summary>
    private bool CanExecuteTerminal => !IsTerminalBusy;

    /// <summary>执行终端命令 — 解析内建命令或启动外部进程</summary>
    [RelayCommand(CanExecute = nameof(CanExecuteTerminal))]
    private async Task ExecuteTerminalAsync() {
        var command = TerminalInput.Trim();
        if (string.IsNullOrEmpty(command))
            return;

        TerminalInput = "";
        _terminalHistory.Add(command);
        _terminalHistoryIndex = -1;
        AppendTerminalLine($"{TerminalPrompt} {command}");

        if (await TryHandleBuiltinCommandAsync(command))
            return;

        await ExecuteExternalCommandAsync(command);
    }

    /// <summary>尝试处理内建命令（cd/cls/clear），返回 true 表示已处理</summary>
    private Task<bool> TryHandleBuiltinCommandAsync(string command) {
        var parts = command.Split(' ', 2, StringSplitOptions.TrimEntries);
        var cmd = parts[0].ToLowerInvariant();
        return cmd switch {
            "cd" or "chdir" => HandleCdAsync(parts.Length > 1 ? parts[1] : ""),
            "cls" or "clear" => HandleClearAsync(),
            _ => Task.FromResult(false)
        };
    }

    /// <summary>处理 cd 命令 — 切换工作目录</summary>
    private Task<bool> HandleCdAsync(string path) {
        var target = string.IsNullOrEmpty(path)
            ? ResolveTerminalWorkingDirectory()
            : System.IO.Path.GetFullPath(System.IO.Path.Combine(ResolveTerminalWorkingDirectory(), path));
        if (!System.IO.Directory.Exists(target))
            AppendTerminalLine($"找不到路径: {target}");
        else {
            _terminalWorkingDirectory = target;
            OnPropertyChanged(nameof(TerminalPrompt));
        }
        return Task.FromResult(true);
    }

    /// <summary>处理 cls/clear 命令 — 清空终端输出</summary>
    private Task<bool> HandleClearAsync() {
        TerminalOutput = "";
        return Task.FromResult(true);
    }

    /// <summary>执行外部命令 — 启动 cmd.exe /c 进程，异步读取 stdout/stderr</summary>
    private async Task ExecuteExternalCommandAsync(string command) {
        IsTerminalBusy = true;
        try {
            var psi = new System.Diagnostics.ProcessStartInfo {
                FileName = "cmd.exe",
                Arguments = $"/c {command}",
                WorkingDirectory = ResolveTerminalWorkingDirectory(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            using var process = new System.Diagnostics.Process { StartInfo = psi };
            process.Start();
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(CancellationToken.None);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            if (!string.IsNullOrEmpty(stdout))
                AppendTerminal(stdout);
            if (!string.IsNullOrEmpty(stderr))
                AppendTerminal(stderr);
        } catch (Exception ex) {
            AppendTerminalLine($"错误: {ex.Message}");
        } finally {
            IsTerminalBusy = false;
        }
    }

    /// <summary>追加终端输出（无换行）</summary>
    private void AppendTerminal(string text) => TerminalOutput += text;

    /// <summary>追加终端输出行（带换行）</summary>
    private void AppendTerminalLine(string text) => TerminalOutput += text + "\n";

    /// <summary>终端输入框历史导航（-1 上一条，1 下一条）</summary>
    [RelayCommand]
    private void NavigateTerminalHistory(int direction) {
        if (_terminalHistory.Count == 0)
            return;
        if (_terminalHistoryIndex == -1 && direction > 0)
            return;
        var next = _terminalHistoryIndex == -1
            ? _terminalHistory.Count - 1
            : _terminalHistoryIndex + direction;
        if (next < 0 || next >= _terminalHistory.Count)
            return;
        _terminalHistoryIndex = next;
        _isTerminalNavigating = true;
        TerminalInput = _terminalHistory[next];
        _isTerminalNavigating = false;
    }

    /// <summary>清空终端输出</summary>
    [RelayCommand]
    private void ClearTerminal() => TerminalOutput = "";
}
