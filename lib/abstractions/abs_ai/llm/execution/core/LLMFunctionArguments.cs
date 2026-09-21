namespace JoinCode.Abstractions.LLM;

public sealed class LLMFunctionArguments : Dictionary<string, JsonElement> {
    /// <summary>构造空函数参数集合。</summary>
    public LLMFunctionArguments() { }

    /// <summary>从字典构造函数参数集合。</summary>
    /// <param name="dictionary">源字典。</param>
    public LLMFunctionArguments(IDictionary<string, JsonElement> dictionary) : base(dictionary) { }
}
