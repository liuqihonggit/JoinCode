namespace JoinCode.Abstractions.LLM;

public sealed class LLMFunctionArguments : Dictionary<string, JsonElement>
{
    public LLMFunctionArguments() { }

    public LLMFunctionArguments(IDictionary<string, JsonElement> dictionary) : base(dictionary) { }
}
