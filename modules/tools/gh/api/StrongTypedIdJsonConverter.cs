namespace McpToolDispatch;

/// <summary>
/// strong-typed ID 的通用 JsonConverter — 用工厂方法创建 ID 实例,构造时校验,失败给出有意义的错误信息
/// <para>AOT 友好: 不用反射,每个 ID 在 JsonContext 注册时传入工厂方法</para>
/// </summary>
internal sealed class StrongTypedIdJsonConverter<T> : JsonConverter<T> where T : struct {
    private readonly Func<long, T> _factory;
    private readonly Func<T, long> _extractor;
    private readonly string _typeName;

    internal StrongTypedIdJsonConverter(Func<long, T> factory, Func<T, long> extractor, string typeName) {
        _factory = factory;
        _extractor = extractor;
        _typeName = typeName;
    }

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        var value = reader.GetInt64();
        try { return _factory(value); }
        catch (ArgumentOutOfRangeException ex) {
            throw new JsonException($"{_typeName} 校验失败: {ex.Message} (值={value})", ex);
        }
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        => writer.WriteNumberValue(_extractor(value));
}

/// <summary>strong-typed ID 注册器 — 在 JsonContext 初始化时批量注册所有 ID converter</summary>
internal static class StrongTypedIdConverterRegistry {
    internal static void Register(JsonSerializerOptions options) {
        options.Converters.Add(new StrongTypedIdJsonConverter<LabelId>(v => new LabelId(v), id => id.Value, nameof(LabelId)));
        options.Converters.Add(new StrongTypedIdJsonConverter<PrId>(v => new PrId(v), id => id.Value, nameof(PrId)));
        options.Converters.Add(new StrongTypedIdJsonConverter<IssueId>(v => new IssueId(v), id => id.Value, nameof(IssueId)));
        options.Converters.Add(new StrongTypedIdJsonConverter<RunId>(v => new RunId(v), id => id.Value, nameof(RunId)));
        options.Converters.Add(new StrongTypedIdJsonConverter<RunJobId>(v => new RunJobId(v), id => id.Value, nameof(RunJobId)));
        options.Converters.Add(new StrongTypedIdJsonConverter<RunArtifactId>(v => new RunArtifactId(v), id => id.Value, nameof(RunArtifactId)));
        options.Converters.Add(new StrongTypedIdJsonConverter<WorkflowId>(v => new WorkflowId(v), id => id.Value, nameof(WorkflowId)));
        options.Converters.Add(new StrongTypedIdJsonConverter<ReleaseId>(v => new ReleaseId(v), id => id.Value, nameof(ReleaseId)));
        options.Converters.Add(new StrongTypedIdJsonConverter<ReleaseAssetId>(v => new ReleaseAssetId(v), id => id.Value, nameof(ReleaseAssetId)));
        options.Converters.Add(new StrongTypedIdJsonConverter<RepoId>(v => new RepoId(v), id => id.Value, nameof(RepoId)));
        options.Converters.Add(new StrongTypedIdJsonConverter<SshKeyId>(v => new SshKeyId(v), id => id.Value, nameof(SshKeyId)));
        options.Converters.Add(new StrongTypedIdJsonConverter<GpgKeyId>(v => new GpgKeyId(v), id => id.Value, nameof(GpgKeyId)));
        options.Converters.Add(new StrongTypedIdJsonConverter<AutolinkId>(v => new AutolinkId(v), id => id.Value, nameof(AutolinkId)));
        options.Converters.Add(new StrongTypedIdJsonConverter<DeployKeyId>(v => new DeployKeyId(v), id => id.Value, nameof(DeployKeyId)));
        options.Converters.Add(new StrongTypedIdJsonConverter<PrNumber>(v => new PrNumber((int)v), id => id.Value, nameof(PrNumber)));
        options.Converters.Add(new StrongTypedIdJsonConverter<IssueNumber>(v => new IssueNumber((int)v), id => id.Value, nameof(IssueNumber)));
        options.Converters.Add(new StrongTypedIdJsonConverter<RunNumber>(v => new RunNumber((int)v), id => id.Value, nameof(RunNumber)));
        options.Converters.Add(new StrongTypedIdJsonConverter<MilestoneNumber>(v => new MilestoneNumber((int)v), id => id.Value, nameof(MilestoneNumber)));
        options.Converters.Add(new StrongTypedIdJsonConverter<CacheId>(v => new CacheId(v), id => id.Value, nameof(CacheId)));
        options.Converters.Add(new StrongTypedIdJsonConverter<RulesetId>(v => new RulesetId(v), id => id.Value, nameof(RulesetId)));
        options.Converters.Add(new StrongTypedIdJsonConverter<DiscussionNumber>(v => new DiscussionNumber((int)v), id => id.Value, nameof(DiscussionNumber)));
        options.Converters.Add(new StrongTypedIdJsonConverter<ProjectNumber>(v => new ProjectNumber((int)v), id => id.Value, nameof(ProjectNumber)));
        options.Converters.Add(new StrongTypedIdJsonConverter<ProjectItemNumber>(v => new ProjectItemNumber((int)v), id => id.Value, nameof(ProjectItemNumber)));
    }
}
