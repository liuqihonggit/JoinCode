namespace JoinCode.Abstractions.LLM;

public interface IToolDef {
    /// <summary>获取工具名称。</summary>
    string Name { get; }
    /// <summary>获取工具描述。</summary>
    string Description { get; }
    /// <summary>获取工具参数列表。</summary>
    IReadOnlyList<IToolParam> Parameters { get; }
}