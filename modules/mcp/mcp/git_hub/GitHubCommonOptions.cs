namespace McpToolDispatch;

/// <summary>
/// GitHub 工具公共参数选项 — repo + working_dir + verbosity + json_fields
/// <para>用 [McpToolOptions] 标记后，源码生成器自动展开为工具参数。</para>
/// </summary>
public sealed record GitHubCommonOptions {
    /// <summary>仓库(可选,默认当前仓库)</summary>
    [McpToolParameter(WellKnownParam.Repo)]
    public string? Repo { get; init; }

    /// <summary>工作目录(可选)</summary>
    [McpToolParameter(WellKnownParam.WorkingDir)]
    public string? WorkingDir { get; init; }

    /// <summary>输出档位</summary>
    [McpToolParameter(WellKnownParam.Verbosity)]
    public int? Verbosity { get; init; }

    /// <summary>JSON 字段过滤</summary>
    [McpToolParameter(WellKnownParam.JsonFields)]
    public string? JsonFields { get; init; }
}
