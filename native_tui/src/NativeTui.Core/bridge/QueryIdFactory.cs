namespace NativeTui.Core.Bridge;

/// <summary>
/// 查询身份生成器（ADR 0165）— 会话内唯一字符串，前台/后台/task 视图共用同一身份。
/// </summary>
public static class QueryIdFactory
{
    /// <summary>生成新的 queryId（形如 q-&lt;guid&gt;）。</summary>
    public static string New() => $"q-{Guid.NewGuid():N}";
}
