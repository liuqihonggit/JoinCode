namespace JoinCode.Abstractions.LLM.Chat;

public static class CacheBreakMarker {
    public const string MetadataKey = "CacheBreak";

    /// <summary>创建包含缓存中断标记的元数据字典。</summary>
    public static IReadOnlyDictionary<string, JsonElement> Create()
        => new Dictionary<string, JsonElement> {
            [MetadataKey] = JsonElementHelper.FromBoolean(true)
        };
}