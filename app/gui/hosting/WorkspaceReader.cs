namespace JoinCode.Gui.Hosting;

/// <summary>本地只读工作区操作，Git 参数通过 ArgumentList 传递。</summary>
internal static class WorkspaceReader {
    /// <summary>读取一层目录，避免递归扫描及 UI 阻塞。</summary>
    internal static IReadOnlyList<WorkspaceFile> List(string directory) =>
        new System.IO.DirectoryInfo(directory).EnumerateFileSystemInfos()
            .Where(e => (e.Attributes & System.IO.FileAttributes.ReparsePoint) == 0)
            .Select(e => new WorkspaceFile(e.Name, e.FullName, e is System.IO.DirectoryInfo))
            .OrderByDescending(e => e.IsDirectory).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>读取有界文本；二进制和过大文件显示可操作提示。</summary>
    internal static async Task<string> ReadAsync(string path) {
        if (new System.IO.FileInfo(path).Length > 2 * 1024 * 1024)
            return "文件超过 2 MB，请使用外部编辑器打开。";
        var bytes = await System.IO.File.ReadAllBytesAsync(path);
        if (bytes.Contains((byte)0)) return "这是二进制文件，请使用对应应用打开。";
        return System.Text.Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
    }

    /// <summary>在指定目录执行只读 Git 命令，完整消费输出和错误。</summary>
    internal static Task<string> GitAsync(string directory, params string[] arguments) => GitAsync(directory, arguments, CancellationToken.None);

    /// <summary>可取消的只读 Git 进程；取消时等待自己启动的进程退出。</summary>
    internal static async Task<string> GitAsync(string directory, string[] arguments, CancellationToken cancellationToken) {
        var start = new System.Diagnostics.ProcessStartInfo("git") {
            WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        start.ArgumentList.Add("--no-pager");
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("无法启动 Git，请检查安装。");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync(cancellationToken); }
        catch (OperationCanceledException) {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            await Task.WhenAll(output, error);
            throw;
        }
        var text = await output;
        var detail = await error;
        if (process.ExitCode != 0) throw new InvalidOperationException($"Git 读取失败：{detail.Trim()}。请选择 Git 仓库目录。");
        return string.IsNullOrWhiteSpace(text) ? "没有变更。" : text;
    }
}
