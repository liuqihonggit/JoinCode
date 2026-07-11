using Microsoft.Extensions.DependencyInjection;

Console.WriteLine("IHttpClientFactory NativeAOT Compatibility Check");

// 验证 1: AddHttpClient() 能正常注册
var services = new ServiceCollection();
services.AddHttpClient();

var provider = services.BuildServiceProvider();

// 验证 2: IHttpClientFactory 能被解析
var factory = provider.GetRequiredService<IHttpClientFactory>();
Console.WriteLine($"IHttpClientFactory resolved: {factory.GetType().FullName}");

// 验证 3: CreateClient() 能返回有效 HttpClient
var client = factory.CreateClient();
Console.WriteLine($"HttpClient created: {client.GetType().FullName}");
Console.WriteLine($"HttpClient BaseAddress: {client.BaseAddress}");

// 验证 4: 命名客户端
services = new ServiceCollection();
services.AddHttpClient("PolicyClient", c =>
{
    c.BaseAddress = new Uri("https://policy.example.com/api/");
    c.Timeout = TimeSpan.FromSeconds(30);
});
provider = services.BuildServiceProvider();
factory = provider.GetRequiredService<IHttpClientFactory>();
var namedClient = factory.CreateClient("PolicyClient");
Console.WriteLine($"Named HttpClient BaseAddress: {namedClient.BaseAddress}");
Console.WriteLine($"Named HttpClient Timeout: {namedClient.Timeout}");

// 验证 5: 类型化客户端（带 Typed HttpClient 模式）
services = new ServiceCollection();
services.AddHttpClient<TestTypedClient>(c =>
{
    c.BaseAddress = new Uri("https://test.example.com/");
});
provider = services.BuildServiceProvider();
var typedClient = provider.GetRequiredService<TestTypedClient>();
Console.WriteLine($"Typed HttpClient BaseAddress: {typedClient.HttpClient.BaseAddress}");

Console.WriteLine("All IHttpClientFactory checks passed.");

public sealed class TestTypedClient(HttpClient httpClient)
{
    public HttpClient HttpClient { get; } = httpClient;
}
