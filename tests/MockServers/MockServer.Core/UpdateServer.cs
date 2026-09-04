namespace MockServer.Core;

/// <summary>
/// 虚拟更新服务器 — 托管 manifest.json + exe 二进制，用于自更新 E2E 测试和本地部署
/// 端点: GET /manifest.json | GET /releases/{version}/jcc.exe | GET /health | GET /shutdown
/// > ADR: 0064
/// </summary>
public sealed class UpdateServer
{
    private readonly int _port;
    private readonly string _contentRoot;
    private WebApplication? _app;
    private CancellationTokenSource _cts = new();

    /// <summary>服务器基础 URL</summary>
    public string Url => $"http://localhost:{_port}";

    /// <summary>
    /// 构造更新服务器
    /// </summary>
    /// <param name="port">监听端口（0=自动分配）</param>
    /// <param name="contentRoot">内容根目录（包含 manifest.json 和 releases/ 子目录）</param>
    public UpdateServer(int port = 0, string? contentRoot = null)
    {
        _port = port == 0 ? GetAvailablePort() : port;
        _contentRoot = contentRoot ?? Path.Combine(AppContext.BaseDirectory, "UpdateContent");
    }

    private static int GetAvailablePort()
    {
        using var tcpListener = new TcpListener(IPAddress.Loopback, 0);
        tcpListener.Start();
        var port = ((IPEndPoint)tcpListener.LocalEndpoint).Port;
        tcpListener.Stop();
        return port;
    }

    /// <summary>
    /// 启动服务器
    /// </summary>
    public Task StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls(Url + "/");
        builder.Logging.ClearProviders();

        _app = builder.Build();

        _app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

        _app.MapGet("/manifest.json", async (HttpContext ctx) =>
        {
            var manifestPath = Path.Combine(_contentRoot, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                ctx.Response.StatusCode = 404;
                return;
            }
            ctx.Response.ContentType = "application/json";
            await ctx.Response.SendFileAsync(manifestPath).ConfigureAwait(false);
        });

        _app.MapGet("/releases/{version}/{fileName}", async (string version, string fileName, HttpContext ctx) =>
        {
            var filePath = Path.Combine(_contentRoot, "releases", version, fileName);
            if (!File.Exists(filePath))
            {
                ctx.Response.StatusCode = 404;
                return;
            }
            ctx.Response.ContentType = "application/octet-stream";
            await ctx.Response.SendFileAsync(filePath).ConfigureAwait(false);
        });

        _app.MapGet("/shutdown", async (HttpContext ctx) =>
        {
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync("""{"status":"shutting_down"}""").ConfigureAwait(false);
            _ = Task.Run(async () =>
            {
                await Task.Delay(100).ConfigureAwait(false);
                await _app!.StopAsync().ConfigureAwait(false);
            });
        });

        _ = _app.RunAsync(_cts.Token);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 停止服务器
    /// </summary>
    public async Task StopAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync().ConfigureAwait(false);
        }
        _cts.Cancel();
    }

    /// <summary>
    /// 生成示例 manifest.json 和占位 exe 文件到 contentRoot
    /// </summary>
    /// <param name="version">版本号</param>
    /// <param name="sha256">SHA256 校验和</param>
    /// <param name="exeContent">exe 二进制内容</param>
    public void GenerateContent(string version, string sha256, byte[] exeContent)
    {
        Directory.CreateDirectory(_contentRoot);
        var releasesDir = Path.Combine(_contentRoot, "releases", version);
        Directory.CreateDirectory(releasesDir);

        var exePath = Path.Combine(releasesDir, "jcc.exe");
        File.WriteAllBytes(exePath, exeContent);

        var manifest = $$"""
        {
          "latestVersion": "{{version}}",
          "channel": "stable",
          "releases": [
            {
              "version": "{{version}}",
              "downloadUrl": "releases/{{version}}/jcc.exe",
              "sha256": "{{sha256}}",
              "sizeBytes": {{exeContent.Length}},
              "releaseNotes": "测试版本 {{version}}",
              "publishedAt": "{{DateTimeOffset.UtcNow:O}}"
            }
          ]
        }
        """;
        File.WriteAllText(Path.Combine(_contentRoot, "manifest.json"), manifest);
    }
}
