namespace Infrastructure.Utils.Text;

public static class JsonArgumentParser
{
    public static Dictionary<string, JsonElement> Parse(string? rawArguments)
    {
        if (string.IsNullOrEmpty(rawArguments))
            return new Dictionary<string, JsonElement>();

        try
        {
            return JsonSerializer.Deserialize(rawArguments, ContractsJsonContext.Default.DictionaryStringJsonElement)
                ?? new Dictionary<string, JsonElement>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, JsonElement>();
        }
    }
}
