namespace McpToolDispatch;

/// <summary>
/// jq 表达式无法执行（语法不在支持子集内 / 上游 JSON 不可解析）。
/// <para>存在的理由：<c>--jq login</c> 这种漏写点号的写法，旧实现会静默跳过该管道阶段并原样打印整份 JSON，
/// 使用者会以为 jq 生效了。改成带原因的异常，由 <c>GhSubCommand.InvokeAndOutputAsync</c> 转成
/// 「错误输出 + 退出码 2」，与参数错误同档。</para>
/// </summary>
internal sealed class JqSyntaxException : Exception {
    /// <summary>用可读原因构造。</summary>
    /// <param name="message">面向使用者的失败原因（含支持的语法清单）。</param>
    public JqSyntaxException(string message) : base(message) { }
}
