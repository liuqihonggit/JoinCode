namespace JoinCode.Gui.WebHost;

/// <summary>
/// DSH 前端资产（ADR 0143）：嵌入资源 <c>dsh_web.zip</c> 首次使用时解包到
/// <c>{JccDirectory}/dsh_web/{版本}/</c>，之后直接复用（meta.json 作为完成标记）。
/// 资产是 DSH 0.2.0-rc.2 的前端构建产物（MIT），由 dsh_parity 工具抓取打包。
/// </summary>
internal static class DshWebAssets {
    /// <summary>资产版本（与 zip 内 meta.json 的 dshVersion 一致）</summary>
    internal const string Version = "0.2.0-rc.2";

    private const string ResourceName = "JoinCode.Gui.dsh_web.zip";
    private static readonly Lock Gate = new();
    private static string? _root;

    /// <summary>缓存必须包含的懒加载分片（旧缓存缺任一分片会让对应面板静默失败）</summary>
    private static readonly string[] RequiredChunks = [
        "plugins/@deepseek-ai/dsh-client-ui-sidebar-terminal/client.terminal.js",
        "plugins/@deepseek-ai/dsh-client-ui-sidebar-documentpreview/client.pdf.js",
        "plugins/@deepseek-ai/dsh-client-ui-sidebar-documentpreview/client.excel.js",
    ];

    /// <summary>解包（幂等）并返回资产根目录</summary>
    /// <param name="fileSystem">文件系统抽象（缓存完整性探测）</param>
    internal static string EnsureExtracted(IFileSystem fileSystem) {
        lock (Gate) {
            if (_root is not null) {
                return _root;
            }
            var target = Path.Combine(AppDataConstants.Paths.JccDirectory, "dsh_web", Version);
            if (!IsComplete(fileSystem, target)) {
                Extract(target);
            }
            _root = target;
            return target;
        }
    }

    /// <summary>缓存是否完整：完成标记 + 全部懒加载分片落盘</summary>
    /// <param name="fileSystem">文件系统抽象</param>
    /// <param name="target">缓存目录</param>
    internal static bool IsComplete(IFileSystem fileSystem, string target) =>
        fileSystem.FileExists(Path.Combine(target, "meta.json"))
        && RequiredChunks.All(chunk => fileSystem.FileExists(Path.Combine(target, chunk.Replace('/', Path.DirectorySeparatorChar))));

    /// <summary>把嵌入 zip 展开到目标目录（覆盖写；不做删除，避免与其他进程竞态）</summary>
    private static void Extract(string target) {
        using var stream = typeof(DshWebAssets).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"嵌入资源缺失: {ResourceName}");
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        Directory.CreateDirectory(target);
        var root = Path.GetFullPath(target) + Path.DirectorySeparatorChar;
        foreach (var entry in archive.Entries) {
            var destination = Path.GetFullPath(Path.Combine(target, entry.FullName));
            if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase)) {
                throw new InvalidOperationException($"zip 条目越界: {entry.FullName}");
            }
            if (entry.FullName.EndsWith('/')) {
                Directory.CreateDirectory(destination);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? target);
            entry.ExtractToFile(destination, overwrite: true);
        }
        GuiDiag.Log($"[DSH-WEB] assets extracted to {target}");
    }
}
