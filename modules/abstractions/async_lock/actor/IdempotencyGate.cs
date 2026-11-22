namespace Core.Utils;

/// <summary>
/// 幂等命令校验门 — 封装 ConsumeLoop 的幂等缓存命中检查,把幂等判断从消费循环抽离(关注点分离)。
/// <para>命中缓存 → 调用命令 TryRestoreFromCache 恢复结果(命令自带 OnSuccess 回调) → 返回 true(跳过 Handle)。</para>
/// <para>未命中或未启用幂等 → 返回 false(执行 Handle)。</para>
/// <para>职责边界:只做缓存命中判断,不执行 Handle,不持有 Channel。</para>
/// </summary>
internal sealed class IdempotencyGate {
    private readonly IIdempotencyStore? _store;

    /// <summary>初始化幂等门</summary>
    /// <param name="store">幂等去重存储(null=不启用幂等,所有命令都返回 false)</param>
    public IdempotencyGate(IIdempotencyStore? store) {
        _store = store;
    }

    /// <summary>是否启用了幂等去重(store 非 null)</summary>
    public bool IsEnabled => _store is not null;

    /// <summary>
    /// 尝试从幂等缓存恢复命令结果。
    /// <para>命令未实现 IRequestCommand 或未启用幂等存储 → 返回 false。</para>
    /// <para>命令实现 IRequestCommand 且存储已缓存其结果 → 调用 TryRestoreFromCache(触发 OnSuccess) → 返回 true。</para>
    /// </summary>
    /// <param name="cmd">待检查命令(可为 null,null 直接返回 false)</param>
    /// <returns>true=命中缓存已恢复(调用方应跳过 Handle);false=未命中(调用方应执行 Handle)</returns>
    public bool TryRestore(object? cmd) {
        if (cmd is null || _store is null || cmd is not IRequestCommand reqCmd) return false;
        return reqCmd.TryRestoreFromCache(_store);
    }
}
