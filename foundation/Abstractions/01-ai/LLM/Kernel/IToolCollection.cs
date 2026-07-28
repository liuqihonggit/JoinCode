namespace JoinCode.Abstractions.LLM;

public interface IToolCollection
{
    IToolGroup? GetPlugin(string name);
    void Add(IToolGroup plugin);
    bool Remove(string name);
    IReadOnlyList<string> PluginNames { get; }
}
