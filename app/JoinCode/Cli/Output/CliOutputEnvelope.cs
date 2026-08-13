namespace JoinCode.Cli.Output;

/// <summary>
/// CLI 输出契约 — stdout 结构化 JSON 输出模型
/// 对齐架构指南：stdout 只输出结构化数据 {ok, data, meta, schema_version}
/// </summary>
public sealed class CliOutputEnvelope
{
    /// <summary>操作是否成功</summary>
    public bool Ok { get; init; }

    /// <summary>成功时的数据负载</summary>
    public object? Data { get; init; }

    /// <summary>失败时的结构化错误</summary>
    public CliStructuredError? Error { get; init; }

    /// <summary>元数据（版本、耗时、分页等）</summary>
    public CliOutputMeta? Meta { get; init; }

    /// <summary>Schema 版本 — 保证向后兼容，消费者可据此选择解析路径</summary>
    public string SchemaVersion { get; init; } = "1";

    public static CliOutputEnvelope Success(object? data, CliOutputMeta? meta = null) =>
        new() { Ok = true, Data = data, Meta = meta };

    public static CliOutputEnvelope Fail(CliStructuredError error, CliOutputMeta? meta = null) =>
        new() { Ok = false, Error = error, Meta = meta };
}

/// <summary>
/// 输出元数据 — 非业务数据，辅助消费方理解上下文
/// </summary>
public sealed class CliOutputMeta
{
    /// <summary>CLI 版本号</summary>
    public string? Version { get; init; }

    /// <summary>命令耗时（毫秒）</summary>
    public long? DurationMs { get; init; }

    /// <summary>分页游标（列表类命令）</summary>
    public string? NextCursor { get; init; }

    /// <summary>总数（列表类命令）</summary>
    public int? TotalCount { get; init; }
}
