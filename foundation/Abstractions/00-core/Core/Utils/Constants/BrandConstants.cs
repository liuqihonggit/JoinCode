namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 品牌常量 — 集中管理产品名、CLI 命令名等品牌标识
/// 禁止在代码中硬编码 "JoinCode" 或 "jcc" 字符串，统一引用此常量
/// </summary>
public static class BrandConstants
{
    /// <summary>
    /// 产品显示名称
    /// </summary>
    public const string ProductName = "JoinCode";

    /// <summary>
    /// CLI 命令名
    /// </summary>
    public const string CliCommandName = "jcc";
}
