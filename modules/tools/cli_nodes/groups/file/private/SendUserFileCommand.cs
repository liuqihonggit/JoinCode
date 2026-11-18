// send_user_file — 纯文件读取 + 字符串摘要，无宿主 TUI 交互。
// 真实实现的 IFileTransferService 只是文本拼接（下载链接是 http://localhost:{端口}/download/... 假链接），
// 无真实 HTTP 服务。exe 用 BCL File/FileInfo 等价复刻。



namespace JoinCode.CliNode.File;

internal static partial class FileCommands {
    private static async Task<int> SendUserFileAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var path = o.TryGetValue("file_path", out var p) && !string.IsNullOrWhiteSpace(p) ? p : "";
        if (path.Length == 0) { Console.Error.WriteLine("send_user_file 缺少必填参数 --file_path。正确写法: jcc tool call file send_user_file --file_path <路径>"); return 2; }
        if (!global::System.IO.File.Exists(path)) { Console.Out.WriteLine($"文件不存在: {path}"); return 0; }
        var info = new FileInfo(path);
        var description = o.TryGetValue("description", out var d) ? d : "";
        var preview = !o.TryGetValue("preview", out var pv) || !bool.TryParse(pv, out var pb) || pb;
        var genLink = o.TryGetValue("generate_link", out var gl) && bool.TryParse(gl, out var gb) && gb;
        var sb = new StringBuilder();
        sb.AppendLine($"文件已发送: {Path.GetFileName(path)}");
        sb.AppendLine($"路径: {path}");
        sb.AppendLine($"大小: {FormatSize(info.Length)}");
        sb.AppendLine($"修改时间: {info.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
        if (!string.IsNullOrEmpty(description)) sb.AppendLine($"描述: {description}");
        if (genLink) {
            var port = Random.Shared.Next(10000, 60000);
            sb.AppendLine();
            sb.AppendLine($"http://localhost:{port}/download/{Uri.EscapeDataString(Path.GetFileName(path))}");
        }
        if (preview && info.Length < 10240) {
            sb.AppendLine();
            sb.AppendLine("文件内容预览:");
            try {
                var lines = global::System.IO.File.ReadAllLines(path);
                sb.Append(string.Join("\n", lines.Take(50)));
                if (lines.Length > 50) sb.AppendLine($"\n... (共 {lines.Length} 行)");
                sb.AppendLine();
                sb.AppendLine("--- 预览结束 ---");
            } catch (Exception ex) { sb.AppendLine($"(预览失败: {ex.Message})"); }
        }
        Console.Out.Write(sb.ToString());
        await Task.CompletedTask.ConfigureAwait(false);
        return 0;
    }

    private static string FormatSize(long bytes) {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }
}
