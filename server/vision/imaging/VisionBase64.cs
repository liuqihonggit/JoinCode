namespace JoinCode.Vision.Imaging;

/// <summary>
/// Vision base64 解码统一入口 — 消除散落在各 ToolHandler 的 Convert.FromBase64String 容错代码
/// 非法输入返回 false + 错误描述，而非抛 FormatException，保证 LLM 传错参数时工具返回友好错误
/// </summary>
public static class VisionBase64
{
    /// <summary>尝试从 base64 解码字节，失败返回错误描述（不抛异常）</summary>
    /// <param name="base64">base64 编码字符串</param>
    /// <param name="bytes">解码成功时的字节，失败时为空数组</param>
    /// <param name="error">解码失败时的错误描述，成功时为空字符串</param>
    /// <returns>解码是否成功</returns>
    public static bool TryDecode(string base64, out byte[] bytes, out string error)
    {
        try
        {
            bytes = Convert.FromBase64String(base64);
            error = string.Empty;
            return true;
        }
        catch (FormatException)
        {
            bytes = Array.Empty<byte>();
            error = "base64 格式无效";
            return false;
        }
    }
}
