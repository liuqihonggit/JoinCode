namespace Mcp.MockServer;

using Mcp.MockServer.Engine;
using Mcp.MockServer.Models;

/// <summary>
/// MCP MockServer 入口 — 通过 HTTP (Streamable HTTP) 接收 JSON-RPC 请求
/// 对齐 MCP 协议: jcc 通过 mcp_connect transport_type=http 连接此服务器
/// </summary>
public sealed class Program
{
    private static readonly ManualResetEventSlim ShutdownEvent = new(false);

    public static async Task Main(string[] args)
    {
        var configPath = ParseArgument(args, "--config") ?? "mockserver.json";
        var portArg = ParseArgument(args, "--port");
        var config = McpMockServerConfig.LoadFromFileOrDefault(configPath);

        var port = int.TryParse(portArg, out var p) ? p : config.Port;
        if (port == 0)
        {
            port = GetAvailablePort();
        }

        Console.WriteLine($"[Mcp.MockServer] Config: {configPath}");
        Console.WriteLine($"[Mcp.MockServer] Port: {port}");
        Console.WriteLine($"[Mcp.MockServer] ServerName: {config.ServerName}");
        Console.WriteLine($"[Mcp.MockServer] ProtocolVersion: {config.ProtocolVersion}");
        Console.WriteLine($"[Mcp.MockServer] Tools: {config.Tools.Count}");

        var engine = new McpMockServerEngine(config);

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://localhost:{port}/");
        var app = builder.Build();

        // GET / — 健康检查
        app.MapGet("/", async (HttpContext ctx) =>
        {
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync("{\"status\":\"ok\"}");
        });

        // GET /shutdown — 关闭服务器
        app.MapGet("/shutdown", async (HttpContext ctx) =>
        {
            Console.WriteLine($"[Mcp.MockServer] Shutdown requested from {ctx.Connection.RemoteIpAddress}");
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync("{\"status\":\"shutting_down\"}");
            ShutdownEvent.Set();
        });

        // POST /mcp — MCP JSON-RPC 端点（Streamable HTTP）
        // 客户端通过 HTTP POST 发送 JSON-RPC 请求，服务器返回 JSON-RPC 响应
        app.MapPost("/mcp", async (HttpContext ctx) =>
        {
            var requestBody = await new StreamReader(ctx.Request.Body).ReadToEndAsync();
            Console.Error.WriteLine($"[Mcp.MockServer] <- {requestBody}");

            string responseJson;
            try
            {
                responseJson = engine.HandleRequest(requestBody) ?? "";
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Mcp.MockServer] EXCEPTION: {ex}");
                ctx.Response.StatusCode = 500;
                ctx.Response.ContentType = "application/json";
                var errBody = $"{{\"error\":\"{ex.Message.Replace("\\", "\\\\").Replace("\"", "\\\"")}\",\"type\":\"{ex.GetType().Name}\"}}";
                await ctx.Response.WriteAsync(errBody);
                return;
            }

            // 通知（无 id）不返回响应体，返回 202 Accepted
            if (string.IsNullOrEmpty(responseJson))
            {
                ctx.Response.StatusCode = 202;
                return;
            }

            ctx.Response.ContentType = "application/json";
            ctx.Response.Headers["Mcp-Session-Id"] = engine.SessionId ?? "mock-session";

            Console.Error.WriteLine($"[Mcp.MockServer] -> {responseJson}");
            await ctx.Response.WriteAsync(responseJson);
        });

        // POST / — 兼容不带 /mcp 路径的请求
        app.MapPost("/", async (HttpContext ctx) =>
        {
            var requestBody = await new StreamReader(ctx.Request.Body).ReadToEndAsync();
            Console.Error.WriteLine($"[Mcp.MockServer] <- {requestBody}");

            var responseJson = engine.HandleRequest(requestBody);

            if (responseJson is null)
            {
                ctx.Response.StatusCode = 202;
                return;
            }

            ctx.Response.ContentType = "application/json";
            ctx.Response.Headers["Mcp-Session-Id"] = engine.SessionId ?? "mock-session";

            Console.Error.WriteLine($"[Mcp.MockServer] -> {responseJson}");
            await ctx.Response.WriteAsync(responseJson);
        });

        await app.StartAsync();
        Console.WriteLine($"[Mcp.MockServer] Listening on http://localhost:{port}/");
        Console.WriteLine($"[Mcp.MockServer] MCP endpoint: http://localhost:{port}/mcp");

        // 等待关闭信号
        ShutdownEvent.Wait(TimeSpan.FromMinutes(30));

        await app.StopAsync();
        Console.WriteLine($"[Mcp.MockServer] Stopped. RequestCount={engine.RequestCount}, ToolCallCount={engine.ToolCallCount}");
    }

    private static int GetAvailablePort()
    {
        using var tcpListener = new TcpListener(IPAddress.Loopback, 0);
        tcpListener.Start();
        var port = ((IPEndPoint)tcpListener.LocalEndpoint).Port;
        tcpListener.Stop();
        return port;
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
