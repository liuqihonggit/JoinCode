
namespace JoinCode.ChatCommands;

public sealed class ModelNameHelper(IModelConfigLoader? modelConfigLoader = null)
{
    private readonly IModelConfigLoader? _modelConfigLoader = modelConfigLoader;

    public string GetCanonicalName(string fullModelName)
    {
        return _modelConfigLoader?.GetCanonicalName(fullModelName) ?? fullModelName;
    }

    internal string FirstPartyNameToCanonical(string fullModelName)
    {
        return _modelConfigLoader?.GetCanonicalName(fullModelName) ?? fullModelName;
    }
}
