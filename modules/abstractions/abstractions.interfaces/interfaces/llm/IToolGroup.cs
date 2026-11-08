namespace JoinCode.Abstractions.LLM;

/// <summary>LLM 工具分组接口。</summary>
public interface IToolGroup {
    /// <summary>获取分组名称。</summary>
    string Name { get; }
    /// <summary>获取分组内的工具函数集合。</summary>
    IEnumerable<IToolDef> Functions { get; }
}
