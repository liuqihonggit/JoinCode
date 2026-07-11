namespace JoinCode.Sdk;

public sealed class JoinCodeOptions
{
    public ProviderKind Provider { get; set; } = ProviderKind.OpenAI;
    public string ModelId { get; set; } = string.Empty;
    public string? ApiKey { get; set; }
    public string? BaseUrl { get; set; }
    public string Language { get; set; } = "zh";
}
