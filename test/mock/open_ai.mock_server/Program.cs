namespace OpenAI.MockServer;

public sealed class Program {
    private static readonly ManualResetEventSlim ShutdownEvent = new(false);

    public static async Task Main(string[] args) {
        try {
            void LogMain(string msg) {
                Console.WriteLine(msg);
                System.Diagnostics.Trace.WriteLine(msg);
            }

            LogMain($"[OpenAI.MockServer] Args.Length={args.Length}, Args=[{string.Join("|", args)}]");
            var configPath = ParseArgument(args, "--config") ?? "mockserver.json";
            var portArg = ParseArgument(args, "--port");
            var noOpenBrowser = Array.IndexOf(args, "--no-open-browser") >= 0;
            LogMain($"[OpenAI.MockServer] configPath={configPath}, portArg={portArg}, openBrowser={!noOpenBrowser}");
            var config = await MockServerConfig.LoadFromFileOrDefault(configPath).ConfigureAwait(true);

            var port = int.TryParse(portArg, out var p) ? p : config.Port;

            LogMain($"[OpenAI.MockServer] Config: {configPath}, Port: {port}, Turns: {config.ScriptedTurns.Count}");

            var strategy = new OpenAIResponseStrategy(config.ScriptedTurns, config.DefaultResponse);
            var cacheSimulator = new PrefixCacheSimulator(
                TokenEstimator.ExtractConversationPrefix,
                TokenEstimator.EstimateFromMessages);

            await using var server = new KestrelMockServer(strategy, cacheSimulator, port, serverName: "OpenAI");
            server.ShutdownRequested += () => {
                LogMain("[OpenAI.MockServer] Shutdown requested");
                ShutdownEvent.Set();
            };
            await server.StartAsync().ConfigureAwait(true);

            // Interactive 模式：请求挂起等待 responder 注入响应，避免 Replay 模式下注入被下一个请求消费（off-by-one）
            server.Controller.SetInteractiveMode();

            // 睡眠/唤醒监控 — 通用 SleepWakeMonitor，唤醒后检查 Kestrel 健康
            global::MockServer.Core.SleepWakeMonitor.OnResume(() => {
                LogMain("[OpenAI.MockServer] 检测到系统唤醒，Kestrel 自动恢复中...");
            });

            LogMain($"[OpenAI.MockServer] Server started, URL={server.Url}, waiting for requests...");

            // 自动打开浏览器 Dashboard
            if (!noOpenBrowser) {
                var dashboardUrl = $"{server.Url}__control__/dashboard";
                LogMain($"[OpenAI.MockServer] Opening browser: {dashboardUrl}");
                try {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
                        FileName = dashboardUrl,
                        UseShellExecute = true,
                    });
                } catch (Exception ex) {
                    LogMain($"[OpenAI.MockServer] Failed to open browser: {ex.Message}");
                }
            }

            ShutdownEvent.Wait(TimeSpan.FromMinutes(30));

            LogMain("[OpenAI.MockServer] ShutdownEvent released, stopping...");
            await server.StopAsync().ConfigureAwait(true);
        } catch (Exception ex) {
            Console.WriteLine($"[OpenAI.MockServer] FATAL: {ex}");
            Environment.ExitCode = 1;
        }
    }

    private static string? ParseArgument(string[] args, string name)
        => CommandLineParser.ParseArgument(args, name);
}