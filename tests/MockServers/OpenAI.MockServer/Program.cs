namespace OpenAI.MockServer;

public sealed class Program
{
    private static readonly ManualResetEventSlim ShutdownEvent = new(false);

    public static async Task Main(string[] args)
    {
        try
        {
            void LogMain(string msg)
            {
                Console.WriteLine(msg);
                System.Diagnostics.Trace.WriteLine(msg);
            }

            LogMain($"[OpenAI.MockServer] Args.Length={args.Length}, Args=[{string.Join("|", args)}]");
            var configPath = ParseArgument(args, "--config") ?? "mockserver.json";
            var portArg = ParseArgument(args, "--port");
            LogMain($"[OpenAI.MockServer] configPath={configPath}, portArg={portArg}");
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

            Console.WriteLine("[OpenAI.MockServer] Server started successfully, waiting for requests...");

            ShutdownEvent.Wait(TimeSpan.FromMinutes(30));

            await server.StopAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OpenAI.MockServer] FATAL: {ex}");
            Environment.ExitCode = 1;
        }
    }

    private static string? ParseArgument(string[] args, string name)
        => CommandLineParser.ParseArgument(args, name);
}
