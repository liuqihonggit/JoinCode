namespace JoinCode.Abstractions.LLM;

public interface IToolGroupFactory {
    /// <summary>从对象实例创建工具组。</summary>
    IToolGroup CreateFromObject(object instance, string pluginName);
}
