namespace Responses.MockServer;

public sealed class Program
{
    private static readonly ManualResetEventSlim ShutdownEvent = new(false);

    public static async Task Main(string[] args)
    {
        var configPath = ParseArgument(args, "--config") ?? "mockserver.json";
        var portArg = ParseArgument(args, "--port");
        var config = MockServerConfig.LoadFromFileOrDefault(configPath);

        var port = int.TryParse(portArg, out var p) ? p : config.Port;

        Console.WriteLine($"[Responses.MockServer] Config: {configPath}");
        Console.WriteLine($"[Responses.MockServer] Requested Port: {port}");
        Console.WriteLine($"[Responses.MockServer] Scripted turns: {config.ScriptedTurns.Count}");

        var strategy = new ResponsesResponseStrategy(config.ScriptedTurns, config.DefaultResponse);
        var cacheSimulator = new PrefixCacheSimulator(
            TokenEstimator.ExtractConversationPrefix,
            TokenEstimator.EstimateFromMessages);

        await using var server = new KestrelMockServer(strategy, cacheSimulator, port, serverName: "Responses");
        server.ShutdownRequested += () => ShutdownEvent.Set();
        await server.StartAsync().ConfigureAwait(false);

        ShutdownEvent.Wait(TimeSpan.FromMinutes(30));

        await server.StopAsync().ConfigureAwait(false);
    }

    private static string? ParseArgument(string[] args, string name)
        => CommandLineParser.ParseArgument(args, name);
}
