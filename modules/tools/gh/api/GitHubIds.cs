namespace McpToolDispatch;

/// <summary>
/// GitHub strong-typed ID — 每种资源一个专属 ID 类型,类型安全+校验+报错+传递语义
/// <para>禁止裸用 int/long 作为 ID 类型 — 用专属结构体防止类型混淆,构造时校验非负非零</para>
/// <para>GitHub API 的数字 ID 是 64 位整数,统一用 long 存储</para>
/// </summary>

public readonly record struct LabelId {
    public long Value { get; }
    public LabelId(long value) {
        if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value), "LabelId must be positive");
        Value = value;
    }
    public override string ToString() => Value.ToString();
}

public readonly record struct PrId {
    public long Value { get; }
    public PrId(long value) {
        if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value), "PrId must be positive");
        Value = value;
    }
    public override string ToString() => Value.ToString();
}

public readonly record struct IssueId {
    public long Value { get; }
    public IssueId(long value) {
        if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value), "IssueId must be positive");
        Value = value;
    }
    public override string ToString() => Value.ToString();
}

public readonly record struct RunId {
    public long Value { get; }
    public RunId(long value) {
        if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value), "RunId must be positive");
        Value = value;
    }
    public override string ToString() => Value.ToString();
}

public readonly record struct RunJobId {
    public long Value { get; }
    public RunJobId(long value) {
        if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value), "RunJobId must be positive");
        Value = value;
    }
    public override string ToString() => Value.ToString();
}

public readonly record struct RunArtifactId {
    public long Value { get; }
    public RunArtifactId(long value) {
        if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value), "RunArtifactId must be positive");
        Value = value;
    }
    public override string ToString() => Value.ToString();
}

public readonly record struct WorkflowId {
    public long Value { get; }
    public WorkflowId(long value) {
        if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value), "WorkflowId must be positive");
        Value = value;
    }
    public override string ToString() => Value.ToString();
}

public readonly record struct ReleaseId {
    public long Value { get; }
    public ReleaseId(long value) {
        if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value), "ReleaseId must be positive");
        Value = value;
    }
    public override string ToString() => Value.ToString();
}

public readonly record struct ReleaseAssetId {
    public long Value { get; }
    public ReleaseAssetId(long value) {
        if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value), "ReleaseAssetId must be positive");
        Value = value;
    }
    public override string ToString() => Value.ToString();
}

public readonly record struct RepoId {
    public long Value { get; }
    public RepoId(long value) {
        if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value), "RepoId must be positive");
        Value = value;
    }
    public override string ToString() => Value.ToString();
}

public readonly record struct SshKeyId {
    public long Value { get; }
    public SshKeyId(long value) {
        if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value), "SshKeyId must be positive");
        Value = value;
    }
    public override string ToString() => Value.ToString();
}

public readonly record struct GpgKeyId {
    public long Value { get; }
    public GpgKeyId(long value) {
        if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value), "GpgKeyId must be positive");
        Value = value;
    }
    public override string ToString() => Value.ToString();
}

public readonly record struct AutolinkId {
    public long Value { get; }
    public AutolinkId(long value) {
        if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value), "AutolinkId must be positive");
        Value = value;
    }
    public override string ToString() => Value.ToString();
}

public readonly record struct DeployKeyId {
    public long Value { get; }
    public DeployKeyId(long value) {
        if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value), "DeployKeyId must be positive");
        Value = value;
    }
    public override string ToString() => Value.ToString();
}

/// <summary>PR/Issue 编号(GitHub 人类可读编号,非 node_id)</summary>
public readonly record struct PrNumber {
    public int Value { get; }
    public PrNumber(int value) {
        if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value), "PrNumber must be positive");
        Value = value;
    }
    public override string ToString() => Value.ToString();
}

/// <summary>Issue 编号(GitHub 人类可读编号,非 node_id)</summary>
public readonly record struct IssueNumber {
    public int Value { get; }
    public IssueNumber(int value) {
        if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value), "IssueNumber must be positive");
        Value = value;
    }
    public override string ToString() => Value.ToString();
}

/// <summary>Run 编号(GitHub 人类可读 run_number,非 id)</summary>
public readonly record struct RunNumber {
    public int Value { get; }
    public RunNumber(int value) {
        if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value), "RunNumber must be positive");
        Value = value;
    }
    public override string ToString() => Value.ToString();
}

/// <summary>Milestone 编号</summary>
public readonly record struct MilestoneNumber {
    public int Value { get; }
    public MilestoneNumber(int value) {
        if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value), "MilestoneNumber must be positive");
        Value = value;
    }
    public override string ToString() => Value.ToString();
}

/// <summary>GitHub Actions cache ID</summary>
public readonly record struct CacheId {
    public long Value { get; }
    public CacheId(long value) {
        if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value), "CacheId must be positive");
        Value = value;
    }
    public override string ToString() => Value.ToString();
}

/// <summary>GitHub Ruleset ID</summary>
public readonly record struct RulesetId {
    public long Value { get; }
    public RulesetId(long value) {
        if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value), "RulesetId must be positive");
        Value = value;
    }
    public override string ToString() => Value.ToString();
}

/// <summary>Discussion 编号</summary>
public readonly record struct DiscussionNumber {
    public int Value { get; }
    public DiscussionNumber(int value) {
        if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value), "DiscussionNumber must be positive");
        Value = value;
    }
    public override string ToString() => Value.ToString();
}

/// <summary>Project 编号</summary>
public readonly record struct ProjectNumber {
    public int Value { get; }
    public ProjectNumber(int value) {
        if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value), "ProjectNumber must be positive");
        Value = value;
    }
    public override string ToString() => Value.ToString();
}

/// <summary>Project item 编号</summary>
public readonly record struct ProjectItemNumber {
    public int Value { get; }
    public ProjectItemNumber(int value) {
        if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value), "ProjectItemNumber must be positive");
        Value = value;
    }
    public override string ToString() => Value.ToString();
}
