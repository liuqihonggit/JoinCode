namespace Tools.Handlers;

/// <summary>
/// tar.gz 解压器 — System.Formats.Tar + GZipStream,自动剥离 GitHub tarball 根目录前缀。
/// <para>GitHub tarball 结构: {owner}-{repo}-{sha}/ 包裹根目录,解压时剥离这层前缀。</para>
/// </summary>
public static class TarExtractor {
    /// <summary>
    /// 解压 .tar.gz 流到目标目录,自动剥离根目录前缀。
    /// </summary>
    /// <param name="tarGzStream">.tar.gz 输入流(从当前位置读取)。</param>
    /// <param name="targetDir">目标目录(不存在则创建)。</param>
    /// <param name="fs">文件系统抽象(生产 PhysicalFileSystem / 测试 InMemoryFileSystem)。</param>
    /// <param name="ct">取消令牌。</param>
    public static async Task ExtractAsync(Stream tarGzStream, string targetDir, IFileSystem fs, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(tarGzStream);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDir);
        ArgumentNullException.ThrowIfNull(fs);
        fs.CreateDirectory(targetDir);

        using var gzip = new GZipStream(tarGzStream, CompressionMode.Decompress);
        using var tar = new System.Formats.Tar.TarReader(gzip);

        string? rootPrefix = null;
        System.Formats.Tar.TarEntry? entry;
        while ((entry = tar.GetNextEntry()) is not null) {
            ct.ThrowIfCancellationRequested();
            var relativePath = ExtractRelativePath(entry.Name, ref rootPrefix);
            if (string.IsNullOrEmpty(relativePath)) continue;

            var fullPath = fs.CombinePath(targetDir, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));

            if (entry.EntryType is System.Formats.Tar.TarEntryType.Directory) {
                fs.CreateDirectory(fullPath);
                continue;
            }

            if (entry.EntryType is System.Formats.Tar.TarEntryType.RegularFile && entry.DataStream is not null) {
                var dir = System.IO.Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir)) fs.CreateDirectory(dir);
                using var outStream = fs.Open(fullPath, FileMode.Create);
                await entry.DataStream.CopyToAsync(outStream, ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// 剥离 GitHub tarball 根目录前缀({owner}-{repo}-{sha}/),返回相对路径。
    /// </summary>
    private static string ExtractRelativePath(string entryName, ref string? rootPrefix) {
        if (rootPrefix is null) {
            var slashIdx = entryName.IndexOf('/');
            if (slashIdx >= 0) {
                rootPrefix = entryName[..(slashIdx + 1)];
            }
        }
        if (rootPrefix is not null && entryName.StartsWith(rootPrefix, StringComparison.Ordinal)) {
            return entryName[rootPrefix.Length..];
        }
        return entryName;
    }
}
