namespace Anthropic.MockServer;

public sealed class Program {
    private static readonly ManualResetEventSlim ShutdownEvent = new(false);

    /// <summary>程序入口点 — 加载配置、启动 Mock 服务器并等待关闭信号</summary>
    public static async Task Main(string[] args) {
        var configPath = ParseArgument(args, "--config") ?? "mockserver.json";
        var portArg = ParseArgument(args, "--port");
        var config = MockServerConfig.LoadFromFileOrDefault(configPath);

        var port = int.TryParse(portArg, out var p) ? p : config.Port;

        Console.WriteLine($"[Anthropic.MockServer] Config: {configPath}");
        Console.WriteLine($"[Anthropic.MockServer] Requested Port: {port}");
        Console.WriteLine($"[Anthropic.MockServer] Scripted turns: {config.ScriptedTurns.Count}");

        var strategy = new AnthropicResponseStrategy(config.ScriptedTurns, config.DefaultResponse);
        var cacheSimulator = new PrefixCacheSimulator(
            TokenEstimator.ExtractConversationPrefix,
            TokenEstimator.EstimateFromMessages);

        await using var server = new KestrelMockServer(strategy, cacheSimulator, port, serverName: "Anthropic");
        server.ShutdownRequested += () => ShutdownEvent.Set();
        await server.StartAsync().ConfigureAwait(true);

        ShutdownEvent.Wait(TimeSpan.FromMinutes(30));

        await server.StopAsync().ConfigureAwait(true);
    }

    private static string? ParseArgument(string[] args, string name)
        => CommandLineParser.ParseArgument(args, name);
}