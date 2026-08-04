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

            LogMain($"[OpenAI.MockServer] Config: {configPath}, Port: {port}, Turns: {config.ScriptedTurns.Count}");

            var strategy = new OpenAIResponseStrategy(config.ScriptedTurns, config.DefaultResponse);
            var cacheSimulator = new PrefixCacheSimulator(
                TokenEstimator.ExtractConversationPrefix,
                TokenEstimator.EstimateFromMessages);

            await using var server = new KestrelMockServer(strategy, cacheSimulator, port, serverName: "OpenAI");
            server.ShutdownRequested += () =>
            {
                LogMain("[OpenAI.MockServer] Shutdown requested");
                ShutdownEvent.Set();
            };
            await server.StartAsync().ConfigureAwait(false);

            LogMain($"[OpenAI.MockServer] Server started, URL={server.Url}, waiting for requests...");

            ShutdownEvent.Wait(TimeSpan.FromMinutes(30));

            LogMain("[OpenAI.MockServer] ShutdownEvent released, stopping...");
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
