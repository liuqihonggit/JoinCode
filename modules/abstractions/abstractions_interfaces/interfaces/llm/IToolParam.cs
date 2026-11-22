namespace JoinCode.Abstractions.LLM;

public interface IToolParam {
    /// <summary>获取参数名称。</summary>
    string Name { get; }
    /// <summary>获取参数描述。</summary>
    string Description { get; }
    /// <summary>获取参数类型。</summary>
    Type? ParameterType { get; }
    /// <summary>获取是否为必需参数。</summary>
    bool IsRequired { get; }
}