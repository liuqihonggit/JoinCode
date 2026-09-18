namespace McpToolDispatch;

/// <summary>
/// GitHub Run 日志提示常量 — 各种场景下的排障步骤提示文本
/// </summary>
internal static class GitHubRunLogHints
{
    /// <summary>
    /// gh_run_list 发现失败 run 时的排障步骤提示
    /// </summary>
    public const string RunListFailureHint =
        "\n\n💡 排障步骤:\n" +
        "1. gh_run_view run_id=xxx expand=jobs → 查看 job 列表(轻量,不下载日志)\n" +
        "2. gh_run_view run_id=xxx expand=failed → 直接拉失败步骤日志\n" +
        "3. gh_run_view run_id=xxx expand=steps job_id=<失败job的ID> → 下载指定 job 日志并查看步骤\n" +
        "4. gh_run_view run_id=xxx log=true job_id=<ID> filter=error → 只看错误行";

    /// <summary>
    /// expand=steps 返回步骤列表后的下一步提示
    /// </summary>
    public const string StepsHint =
        "\n\n💡 下一步:\n" +
        "- expand=step:步骤名 → 查看具体步骤日志\n" +
        "- filter=error → 只看 ##[error] 标记行\n" +
        "- log=true job_id=xxx filter=error → 直接过滤错误行";

    /// <summary>
    /// expand=step:Name 返回 section 摘要后的下一步提示(ADR 0067 Level 2)
    /// </summary>
    public const string SectionHint =
        "\n\n💡 下一步:\n" +
        "- /section:error → 查看 error 段(排障首要)\n" +
        "- /section:group → 查看 group 段(命令上下文)\n" +
        "- /section:normal → 查看普通日志(测试结果)";

    /// <summary>
    /// 日志被截断时的缩小范围提示
    /// </summary>
    public const string TruncatedHint =
        "\n\n💡 日志已截断，缩小范围:\n" +
        "- skip_lines=N → 续读后续行(截断提示中有具体值)\n" +
        "- filter=error → 只看错误行\n" +
        "- 增大 max_lines → 看更多行";

    /// <summary>
    /// expand=failed 返回失败步骤后的下一步提示
    /// </summary>
    public const string FailedHint =
        "\n\n💡 下一步: expand=step:步骤名 → 查看具体步骤的完整日志";

    /// <summary>
    /// 常规 log=true 模式的建议提示
    /// </summary>
    public const string LogHint =
        "\n\n💡 日志量大时建议:\n" +
        "- expand=steps → 按步骤展开\n" +
        "- filter=error → 只看错误行\n" +
        "- skip_lines=N → 分页续读";

    /// <summary>
    /// 缺少栈帧信息提示 — CI 只报 "Process completed with exit code 1" 但无栈帧,引导 AI 按顺序排错
    /// </summary>
    public const string NoStackTraceHint =
        "\n\n⚠️ 运行错误非 0,但缺少栈帧信息,无法定位相关错误。请按顺序逐个排错:\n" +
        "1. 死锁问题: 可能是线程锁超时但缺少抛出对应错误,请查阅工程中涉及超时的代码,修改超时时间到 5s,这样必然等候一段时间的真实死锁\n" +
        "2. CI 配置造成的问题: 检查 workflow YAML、环境变量、缓存配置是否正确\n" +
        "3. GitHub CI 异常: 重试错误发生的工程,可能是 GitHub CI 环境本身存在异常";
}
