namespace JoinCode.Abstractions.Prompts;

/// <summary>
/// 系统提示词部分 - 表示系统提示词的一个可组合部分
/// </summary>
public sealed class SystemPromptSection {
    public string Name { get; }
    public Func<string?> Compute { get; }
    public Func<Task<string?>>? ComputeAsync { get; }
    public bool CacheBreak { get; }

    private SystemPromptSection(string name, Func<string?> compute, Func<Task<string?>>? computeAsync, bool cacheBreak) {
        Name = name;
        Compute = compute;
        ComputeAsync = computeAsync;
        CacheBreak = cacheBreak;
    }

    /// <summary>
    /// 异步计算内容 — 优先使用 ComputeAsync，避免同步阻塞
    /// </summary>
    public ValueTask<string?> ComputeValueTaskAsync() {
        if (ComputeAsync is not null)
            return new ValueTask<string?>(ComputeAsync());
        return new ValueTask<string?>(Compute());
    }

    /// <summary>
    /// 创建缓存的系统提示词部分（计算一次，缓存直到 /clear）
    /// </summary>
    public static SystemPromptSection Cached(string name, Func<string?> compute) {
        return new SystemPromptSection(name, compute, computeAsync: null, cacheBreak: false);
    }

    /// <summary>
    /// 创建缓存的系统提示词部分（异步计算）
    /// </summary>
    public static SystemPromptSection Cached(string name, Func<Task<string?>> computeAsync) {
        return new SystemPromptSection(name, () => computeAsync().GetAwaiter().GetResult(), computeAsync, cacheBreak: false);
    }

    /// <summary>
    /// 创建动态的系统提示词部分（每轮重新计算）
    /// </summary>
    public static SystemPromptSection Dynamic(string name, Func<string?> compute) {
        return new SystemPromptSection(name, compute, computeAsync: null, cacheBreak: true);
    }

    /// <summary>
    /// 创建动态的系统提示词部分（异步计算，每轮重新计算）
    /// </summary>
    public static SystemPromptSection Dynamic(string name, Func<Task<string?>> computeAsync) {
        return new SystemPromptSection(name, () => computeAsync().GetAwaiter().GetResult(), computeAsync, cacheBreak: true);
    }
}
