namespace MockServer.Core;

/// <summary>
/// 命令行参数解析工具 — 统一所有 MockServer 的参数解析逻辑
/// 支持 --name value 和 --name=value 两种格式
/// </summary>
public static class CommandLineParser
{
    /// <summary>
    /// 解析命令行参数 — 支持 --name value（空格分隔）和 --name=value（等号分隔）两种格式
    /// </summary>
    /// <param name="args">命令行参数数组</param>
    /// <param name="name">参数名（如 "--port"）</param>
    /// <returns>参数值，未找到返回 null</returns>
    public static string? ParseArgument(string[] args, string name)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == name && i + 1 < args.Length)
                return args[i + 1];

            if (args[i].StartsWith(name + "=", StringComparison.Ordinal))
                return args[i][(name.Length + 1)..];
        }

        return null;
    }
}
