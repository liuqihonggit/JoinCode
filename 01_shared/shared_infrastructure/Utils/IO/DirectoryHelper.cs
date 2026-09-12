namespace Infrastructure.Utils.IO;

/// <summary>
/// 目录操作辅助类 - 统一 EnsureDirectoryExists 模式
/// </summary>
public static class DirectoryHelper
{
    /// <summary>
    /// 确保目录存在，如果不存在则创建（含所有父目录）— 通过 IFileSystem 抽象
    /// </summary>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="path">目录路径，为 null 或空时直接返回</param>
    public static void EnsureDirectoryExists(IFileSystem fs, string? path)
    {
        ArgumentNullException.ThrowIfNull(fs);
        if (string.IsNullOrEmpty(path) || fs.DirectoryExists(path)) return;
        fs.CreateDirectory(path);
    }

    /// <summary>
    /// 获取相对于基准路径的相对路径，失败时返回原路径
    /// </summary>
    /// <param name="relativeTo">基准路径</param>
    /// <param name="path">目标路径</param>
    public static string GetRelativePath(string relativeTo, string path)
    {
        try
        {
            return Path.GetRelativePath(relativeTo, path);
        }
        catch
        {
            return path;
        }
    }
}
