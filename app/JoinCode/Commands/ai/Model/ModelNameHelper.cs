
namespace JoinCode.ChatCommands;

public static class ModelNameHelper
{
    public static string GetCanonicalName(string fullModelName)
    {
        return ModelConfigLoader.GetCanonicalName(fullModelName);
    }

    internal static string FirstPartyNameToCanonical(string fullModelName)
    {
        return ModelConfigLoader.GetCanonicalName(fullModelName);
    }
}
