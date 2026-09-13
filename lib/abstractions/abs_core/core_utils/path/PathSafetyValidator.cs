
namespace JoinCode.Abstractions.Utils.Path;

/// <summary>
/// 路径安全验证器 — 检测根目录/不安全路径，防止扫盘卡死
/// <para>统一各文件遍历命令的路径安全检查（当前仅 RgSubCommand 使用，未来可扩展到其他命令）</para>
/// </summary>
public static class PathSafetyValidator
{
    /// <summary>
    /// 判断是否为文件系统根目录（如 C:\ / D:\ / /）
    /// </summary>
    public static bool IsRootPath(string fullPath)
    {
        if (string.IsNullOrEmpty(fullPath))
            return false;
        if (fullPath.Length == 1 && fullPath[0] == '/')
            return true;
        if (fullPath.Length == 3 && char.IsLetter(fullPath[0]) && fullPath[1] == ':' && (fullPath[2] == '\\' || fullPath[2] == '/'))
            return true;
        if (fullPath.Length == 2 && char.IsLetter(fullPath[0]) && fullPath[1] == ':')
            return true;
        return false;
    }

    /// <summary>
    /// 判断路径是否为根目录或不安全（null/空/异常路径）
    /// </summary>
    public static bool IsRootOrUnsafePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "." || path == "./")
            return IsRootPath(Environment.CurrentDirectory);

        try
        {
            var fullPath = System.IO.Path.GetFullPath(path);
            return IsRootPath(fullPath);
        }
        catch (Exception)
        {
            return true;
        }
    }
}
