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

    /// <summary>解包（幂等）并返回资产根目录</summary>
    internal static string EnsureExtracted() {
        lock (Gate) {
            if (_root is not null) {
                return _root;
            }
            var target = Path.Combine(AppDataConstants.Paths.JccDirectory, "dsh_web", Version);
            if (!File.Exists(Path.Combine(target, "meta.json"))) {
                Extract(target);
            }
            _root = target;
            return target;
        }
    }

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
        App.LogDiag($"[DSH-WEB] assets extracted to {target}");
    }
}
