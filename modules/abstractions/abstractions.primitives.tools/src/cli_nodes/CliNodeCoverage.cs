namespace JoinCode.Abstractions.Tools;

/// <summary>
/// 本地实现覆盖自报 —— <c>&lt;分组 exe&gt; --list-local</c> 的输出格式。
/// <para>存在理由：<c>node.yaml</c> 里的 <c>mode: local</c> 是从私有实现反推出来的<b>声明</b>，
/// 而"声明"和"真实注册表"可能漂移（改了实现忘了改登记、或反过来）。让 exe 自报实际注册表，
/// CI 就能把两者对齐校验，交付说明里的"已本地实现 N 个"才不是一句无法验真的话。</para>
/// <para>格式刻意用单行 key=value，便于脚本 diff：<c>group=file local_count=11 local=a,b,c</c>。</para>
/// </summary>
public static class CliNodeCoverage {
    /// <summary>自报开关。</summary>
    public const string ListLocalFlag = "--list-local";

    /// <summary>判断首个参数是否在请求自报本地覆盖。</summary>
    /// <param name="arg">命令行首个参数。</param>
    /// <returns>是则 true。</returns>
    public static bool IsRequested(string? arg) =>
        string.Equals(arg, ListLocalFlag, StringComparison.Ordinal);

    /// <summary>渲染单行自报结果。</summary>
    /// <param name="group">分组 ID。</param>
    /// <param name="localToolIds">该 exe 内真实注册的本地工具 ID。</param>
    /// <returns>单行 <c>group=... local_count=... local=...</c>。</returns>
    public static string Render(string group, IEnumerable<string> localToolIds) {
        var ids = localToolIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        return $"group={group} local_count={ids.Length} local={string.Join(",", ids)}{Environment.NewLine}";
    }
}
