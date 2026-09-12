namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 跨会话克隆上下文 — 维护引用重映射表(原ObjectId → 新ObjectId)
/// 克隆时按依赖顺序: 先克隆被引用的Entity, 再克隆引用方
/// 引用找不到时抛异常, 避免悬空引用
/// </summary>
public sealed class CloneContext
{
    private readonly Dictionary<ObjectId, ObjectId> _idMapping = new();

    /// <summary>目标会话 ObjectId</summary>
    public ObjectId TargetSessionId { get; }

    public CloneContext(ObjectId targetSessionId)
    {
        if (targetSessionId.IsEmpty)
            throw new ArgumentException("目标 SessionId 不能为空", nameof(targetSessionId));
        TargetSessionId = targetSessionId;
    }

    /// <summary>记录映射 — 每克隆一个 Entity 调用一次</summary>
    public void Map(ObjectId original, ObjectId cloned)
    {
        if (!original.IsEmpty)
            _idMapping[original] = cloned;
    }

    /// <summary>查找映射 — 找不到返回 ObjectId.Empty</summary>
    public ObjectId Remap(ObjectId original)
    {
        return original.IsEmpty ? original : _idMapping.GetValueOrDefault(original);
    }

    /// <summary>重映射可空引用 — null 或 Empty 原样返回</summary>
    public ObjectId? RemapNullable(ObjectId? original)
    {
        if (original is not { } id || id.IsEmpty)
            return original;
        return _idMapping.TryGetValue(id, out var mapped) ? mapped : null;
    }

    /// <summary>重映射或抛异常 — 引用未先克隆则报错</summary>
    public ObjectId RemapOrThrow(ObjectId original)
    {
        if (original.IsEmpty)
            return original;
        if (!_idMapping.TryGetValue(original, out var mapped))
            throw new InvalidOperationException(
                $"跨会话克隆失败: 引用 {original} 未在目标会话中找到对应 Entity。" +
                $"请先克隆被引用的 Entity, 再克隆引用方.");
        return mapped;
    }

    /// <summary>重映射可空引用或抛异常 — null 或 Empty 原样返回, 否则找不到抛异常</summary>
    public ObjectId? RemapNullableOrThrow(ObjectId? original)
    {
        if (original is not { } id || id.IsEmpty)
            return original;
        if (!_idMapping.TryGetValue(id, out var mapped))
            throw new InvalidOperationException(
                $"跨会话克隆失败: 引用 {id} 未在目标会话中找到对应 Entity。" +
                $"请先克隆被引用的 Entity, 再克隆引用方.");
        return mapped;
    }
}
