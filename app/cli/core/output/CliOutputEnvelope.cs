namespace JoinCode.Cli.Output;

/// <summary>
/// CLI 输出契约 — stdout 结构化 JSON 输出模型
/// 对齐架构指南：stdout 只输出结构化数据 {ok, data, meta, schema_version}
/// <para>非泛型版本仅用于 <see cref="Fail"/>（Data 为 null，不触发多态序列化）。
/// 成功响应必须用 <see cref="CliOutputEnvelope{T}"/>.Success — 编译期保证 Data 类型已注册 JsonSerializable。</para>
/// </summary>
public sealed class CliOutputEnvelope {
    /// <summary>操作是否成功</summary>
    public bool Ok { get; init; }

    /// <summary>成功时的数据负载 — 非泛型版本始终为 null，仅用于 Fail 路径</summary>
    public object? Data { get; init; }

    /// <summary>失败时的结构化错误</summary>
    public CliStructuredError? Error { get; init; }

    /// <summary>元数据（版本、耗时、分页等）</summary>
    public CliOutputMeta? Meta { get; init; }

    /// <summary>Schema 版本 — 保证向后兼容，消费者可据此选择解析路径</summary>
    public string SchemaVersion { get; init; } = "1";

    /// <summary>构造失败响应信封</summary>
    /// <param name="error">结构化错误</param>
    /// <param name="meta">可选的元数据</param>
    /// <returns>Ok 为 false 的 <see cref="CliOutputEnvelope"/> 实例</returns>
    public static CliOutputEnvelope Fail(CliStructuredError error, CliOutputMeta? meta = null) =>
        new() { Ok = false, Error = error, Meta = meta };

    /// <summary>序列化为 JSON 字符串 — 使用 RelaxedJsonSerializer + CliOutputJsonContext(AOT 兼容)</summary>
    /// <returns>符合 {ok, data, error, meta, schemaVersion} 结构的 JSON 字符串</returns>
    public override string ToString() =>
        RelaxedJsonSerializer.Serialize(this, CliOutputJsonContext.Default);
}

/// <summary>
/// CLI 输出信封（泛型）— 成功响应的强类型 Data，编译期保证序列化安全。
/// <para><typeparamref name="T"/> 必须在 <see cref="CliOutputJsonContext"/> 注册
/// <c>[JsonSerializable(typeof(CliOutputEnvelope&lt;T&gt;))]</c>，否则 ToJsonString 运行时报错。
/// 泛型化目的：传 JsonObject/JsonNode 等未注册类型时编译报错（无 Success 重载）。</para>
/// </summary>
/// <typeparam name="T">Data 负载的类型，必须在 CliOutputJsonContext 注册。</typeparam>
public sealed class CliOutputEnvelope<T> {
    /// <summary>操作是否成功</summary>
    public bool Ok { get; init; }

    /// <summary>成功时的强类型数据负载</summary>
    public T? Data { get; init; }

    /// <summary>失败时的结构化错误</summary>
    public CliStructuredError? Error { get; init; }

    /// <summary>元数据（版本、耗时、分页等）</summary>
    public CliOutputMeta? Meta { get; init; }

    /// <summary>Schema 版本</summary>
    public string SchemaVersion { get; init; } = "1";

    /// <summary>构造成功响应信封 — Data 强类型，编译期保证 T 已注册</summary>
    /// <param name="data">成功时的数据负载</param>
    /// <param name="meta">可选的元数据</param>
    /// <returns>Ok 为 true 的 <see cref="CliOutputEnvelope{T}"/> 实例</returns>
    public static CliOutputEnvelope<T> Success(T data, CliOutputMeta? meta = null) =>
        new() { Ok = true, Data = data, Meta = meta };

    /// <summary>序列化为 JSON 字符串 — T 必须在 CliOutputJsonContext 注册 CliOutputEnvelope&lt;T&gt;</summary>
    /// <returns>符合 {ok, data, error, meta, schemaVersion} 结构的 JSON 字符串</returns>
    public string ToJsonString() =>
        RelaxedJsonSerializer.Serialize(this, CliOutputJsonContext.Default);
}

/// <summary>
/// 输出元数据 — 非业务数据，辅助消费方理解上下文
/// </summary>
public sealed class CliOutputMeta {
    /// <summary>CLI 版本号</summary>
    public string? Version { get; init; }

    /// <summary>命令耗时（毫秒）</summary>
    public long? DurationMs { get; init; }

    /// <summary>分页游标（列表类命令）</summary>
    public string? NextCursor { get; init; }

    /// <summary>总数（列表类命令）</summary>
    public int? TotalCount { get; init; }
}

/// <summary>非交互模式运行结果 — 替代匿名类型，AOT 兼容</summary>
public sealed record CliNonInteractiveResult(int ExitCode, string Response);

/// <summary>CLI schema 属性 DTO — 手写镜像 CliArgSchemaProperty（生成器生成），确保 JsonSourceGeneration 可见</summary>
public sealed record CliSchemaPropertyDto(
    string Name,
    string ShortName,
    string Description,
    string Type,
    bool AcceptsValue,
    string? RiskLevel,
    string? Category,
    string? Example);

/// <summary>CLI schema 自省结果 DTO — 用泛型 CliOutputEnvelope&lt;T&gt; 序列化，AOT 兼容</summary>
public sealed record CliSchemaResult(CliSchemaPropertyDto[] Properties);

/// <summary>slash_schema 降级输出 DTO — 命令未声明结构化参数 schema 时，输出 argumentHint 提示</summary>
public sealed record CliSlashSchemaHintResult(string Command, JoinCode.Abstractions.Tools.ToolSchema? Schema, string? ArgumentHint);
