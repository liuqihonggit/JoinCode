namespace JoinCode.Abstractions.LLM.Chat;

public static class CacheBreakMarker
{
    public const string MetadataKey = "CacheBreak";

    public static IReadOnlyDictionary<string, JsonElement> Create()
        => new Dictionary<string, JsonElement>
        {
            [MetadataKey] = JsonElementHelper.FromBoolean(true)
        };
}
