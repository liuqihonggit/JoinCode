namespace JoinCode.Abstractions.LLM;

public interface IToolGroupFactory
{
    IToolGroup CreateFromObject(object instance, string pluginName);
}
