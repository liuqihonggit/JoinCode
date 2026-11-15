namespace JoinCode.Abstractions.Tools;

/// <summary>
/// 分组节点内一个子工具的执行方式 —— 决定它是真进程隔离，还是绕回宿主执行。
/// <para>这个字段存在的意义是<b>可审计</b>：没有它，"本地优先 + 转发兜底"只是一句口头承诺，
/// LLM、CI、审计脚本都无法判断某个工具究竟跑在哪个进程里。</para>
/// </summary>
public enum CliExecutionMode {
    /// <summary>在分组 exe 自己的进程内实现（纯 BCL），离开宿主也能跑 —— 真进程隔离。</summary>
    Local,

    /// <summary>依赖宿主引擎状态（会话 / DI / LLM / 浏览器 / 调度器），exe 内转发宿主执行。</summary>
    Host
}

/// <summary>
/// 分组节点内一个子工具的条目：ID + 执行方式。
/// </summary>
public sealed record CliToolEntry {
    /// <summary>工具 ID（CLI 子命令名，如 file_read）。</summary>
    public required string Id { get; init; }

    /// <summary>执行方式；本地实现 = <see cref="CliExecutionMode.Local"/>。</summary>
    public required CliExecutionMode Mode { get; init; }

    /// <summary>
    /// 别名（斜杠命令常见，如 /clear 的 reset/new/cls）。
    /// 别名与 ID 等价可调用，因此查找与执行前解析都必须认它，否则 LLM 用别名会"工具不存在"。
    /// </summary>
    public string[] Aliases { get; init; } = [];

    /// <summary>是否真正进程隔离（本地实现）。</summary>
    public bool IsLocal => Mode == CliExecutionMode.Local;

    /// <summary>该条目是否匹配给定名称（ID 或任一别名，大小写不敏感）。</summary>
    /// <param name="name">待查名称。</param>
    /// <returns>命中为 true。</returns>
    public bool Matches(string name) =>
        string.Equals(Id, name, StringComparison.OrdinalIgnoreCase)
        || Aliases.Any(alias => string.Equals(alias, name, StringComparison.OrdinalIgnoreCase));
}
