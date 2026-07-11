namespace OpenAI.MockServer;

public sealed class Program
{
    private static readonly ManualResetEventSlim ShutdownEvent = new(false);

    public static async Task Main(string[] args)
    {
        var configPath = ParseArgument(args, "--config") ?? "mockserver.json";
        var portArg = ParseArgument(args, "--port");
        var config = MockServerConfig.LoadFromFileOrDefault(configPath);

        var port = int.TryParse(portArg, out var p) ? p : config.Port;

        Console.WriteLine($"[OpenAI.MockServer] Config: {configPath}");
        Console.WriteLine($"[OpenAI.MockServer] Requested Port: {port}");
        Console.WriteLine($"[OpenAI.MockServer] Scripted turns: {config.ScriptedTurns.Count}");

        var strategy = new OpenAIResponseStrategy(config.ScriptedTurns, config.DefaultResponse);
        var cacheSimulator = new PrefixCacheSimulator(
            TokenEstimator.ExtractConversationPrefix,
            TokenEstimator.EstimateFromMessages);

        await using var server = new KestrelMockServer(strategy, cacheSimulator, port, serverName: "OpenAI");
        server.ShutdownRequested += () => ShutdownEvent.Set();
        await server.StartAsync().ConfigureAwait(false);

        ShutdownEvent.Wait(TimeSpan.FromMinutes(30));

        await server.StopAsync().ConfigureAwait(false);
    }

    private static string? ParseArgument(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == name)
                return args[i + 1];
        }
        return null;
    }
}
