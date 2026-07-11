namespace Testing.Common.MockServer;

/// <summary>
/// Mock Server 配置参数封装类
/// </summary>
public sealed record MockServerOptions
{
    public const string DefaultApiKey = "sk-test-1234567890";
    public const string DefaultModel = "gpt-4o";

    public string PipeName { get; }
    public string ApiKey { get; }
    public string Model { get; }

    public MockServerOptions(string pipeName, string? apiKey = null, string? model = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(pipeName);
        PipeName = pipeName;
        ApiKey = apiKey ?? DefaultApiKey;
        Model = model ?? DefaultModel;
    }
}
