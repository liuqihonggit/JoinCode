namespace JoinCode.Abstractions.Tools;

/// <summary>
/// 分组节点内一个子工具的执行方式 —— 决定它是真进程隔离，还是绕回宿主执行。
/// <para>这个字段存在的意义是<b>可审计</b>：没有它，"本地优先 + 转发兜底"只是一句口头承诺，
/// LLM、CI、审计脚本都无法判断某个工具究竟跑在哪个进程里。</para>
/// </summary>
public enum CliExecutionMode {
    /// <summary>在分组 exe 自己的进程内实现（纯 BCL），离开宿主也能跑 —— 真进程隔离。</summary>
    Local,

    /// <summary>
    /// 委托另一个独立 exe 执行（如 github 组 → jcc-gh.exe）。
    /// <para>同样是跨进程隔离，且不经过宿主的引擎与 Hub；区别只在于实现放在哪个可执行文件里，
    /// 因此不能并入 <see cref="Host"/>，否则审计会把「已隔离」看成「还在宿主里」。</para>
    /// </summary>
    Delegate,

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

    /// <summary>是否真正进程隔离（本地实现或委托独立 exe，都不经过宿主）。</summary>
    public bool IsLocal => Mode is CliExecutionMode.Local or CliExecutionMode.Delegate;

    /// <summary>执行方式的可读标签，用于 node.yaml 展示与审计输出。</summary>
    public string ModeLabel => Mode switch {
        CliExecutionMode.Local => "local",
        CliExecutionMode.Delegate => "delegate",
        _ => "host"
    };

    /// <summary>
    /// 解析时 raw YAML 中显式出现过的键名 — 供继承合并判断 Aliases 等字段"省略 vs 显式"。
    /// 手工 new 的条目集合为空，视为全部字段显式。
    /// </summary>
    internal IReadOnlySet<string> DeclaredKeys { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>该条目是否匹配给定名称（ID 或任一别名，大小写不敏感）。</summary>
    /// <param name="name">待查名称。</param>
    /// <returns>命中为 true。</returns>
    public bool Matches(string name) =>
        NameEquals(Id, name) || Aliases.Any(alias => NameEquals(alias, name));

    /// <summary>
    /// 名称等价判定，额外把前导 <c>/</c> 视作同一名称。
    /// <para>斜杠命令的 ID 写作 <c>/agents</c>，但 Git Bash 会把以 <c>/</c> 开头的参数展开成安装根下的路径，
    /// 调用方还原后通常只剩 <c>agents</c>；不互认的话 <c>jcc tool call slash /agents</c> 与
    /// <c>jcc tool call slash agents</c> 两种写法都会「工具不存在」。</para>
    /// </summary>
    private static bool NameEquals(string candidate, string name) =>
        string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase)
        || string.Equals(candidate.TrimStart('/'), name.TrimStart('/'), StringComparison.OrdinalIgnoreCase);
}
