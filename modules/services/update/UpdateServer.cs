namespace Services.Update;

/// <summary>
/// 虚拟更新服务器 — 托管 manifest.json + exe 二进制，用于自更新 E2E 测试和本地部署
/// 端点: GET /manifest.json | GET /releases/{version}/jcc.exe | GET /health | GET /shutdown
/// > ADR: 0064
/// </summary>
public sealed class UpdateServer {
    private readonly int _port;
    private readonly string _contentRoot;
    private readonly IFileSystem _fs;
    private WebApplication? _app;
    private readonly CancellationTokenSource _cts = new();

    /// <summary>服务器基础 URL</summary>
    public string Url => $"http://localhost:{_port}";

    /// <summary>
    /// 私有构造 — 仅字段赋值，网络 IO（GetAvailablePort）由静态工厂完成
    /// <para>消除半构造化：GetAvailablePort 抛 SocketException 时对象未创建，无悬垂字段。</para>
    /// </summary>
    private UpdateServer(IFileSystem fs, int port, string contentRoot) {
        _fs = fs;
        _port = port;
        _contentRoot = contentRoot;
    }

    /// <summary>
    /// 创建更新服务器 — 端口 0 表示自动分配可用端口
    /// </summary>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="port">监听端口（0=自动分配）</param>
    /// <param name="contentRoot">内容根目录（包含 manifest.json 和 releases/ 子目录）</param>
    public static UpdateServer Create(IFileSystem fs, int port = 0, string? contentRoot = null) {
        ArgumentNullException.ThrowIfNull(fs);
        var actualPort = port == 0 ? GetAvailablePort() : port;
        var actualContentRoot = contentRoot ?? AppDataConstants.Paths.UpdateContentDirectory;
        return new UpdateServer(fs, actualPort, actualContentRoot);
    }

    private static int GetAvailablePort() {
        using var tcpListener = new TcpListener(IPAddress.Loopback, 0);
        tcpListener.Start();
        var port = ((IPEndPoint)tcpListener.LocalEndpoint).Port;
        tcpListener.Stop();
        return port;
    }

    /// <summary>
    /// 启动服务器
    /// </summary>

    public Task StartAsync() {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls(Url + "/");
        builder.Logging.ClearProviders();

        _app = builder.Build();

        _app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

        _app.MapGet("/manifest.json", async (HttpContext ctx) => {
            var manifestPath = _fs.CombinePath(_contentRoot, "manifest.json");
            if (!_fs.FileExists(manifestPath)) {
                ctx.Response.StatusCode = 404;
                return;
            }
            ctx.Response.ContentType = "application/json";
            await using var stream = _fs.OpenRead(manifestPath);
            await stream.CopyToAsync(ctx.Response.Body).ConfigureAwait(false);
        });

        _app.MapGet("/releases/{version}/{fileName}", async (string version, string fileName, HttpContext ctx) => {
            var filePath = _fs.CombinePath(_contentRoot, "releases", version, fileName);
            if (!_fs.FileExists(filePath)) {
                ctx.Response.StatusCode = 404;
                return;
            }
            ctx.Response.ContentType = "application/octet-stream";
            await using var stream = _fs.OpenRead(filePath);
            await stream.CopyToAsync(ctx.Response.Body).ConfigureAwait(false);
        });

        _app.MapGet("/shutdown", async (HttpContext ctx) => {
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync("""{"status":"shutting_down"}""").ConfigureAwait(false);
            _ = FireAndForgetTask<Unit>.Run(async ct => {
                await Task.Delay(100).ConfigureAwait(false);
                await _app.StopAsync().ConfigureAwait(false);
            }, "update-server-shutdown");
        });

        _ = _app.RunAsync();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 停止服务器
    /// </summary>
    public async Task StopAsync() {
        if (_app is not null) {
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
    public async ValueTask GenerateContent(string version, string sha256, byte[] exeContent) {
        _fs.CreateDirectory(_contentRoot);
        var releasesDir = _fs.CombinePath(_contentRoot, "releases", version);
        _fs.CreateDirectory(releasesDir);

        var exePath = _fs.CombinePath(releasesDir, "jcc.exe");
        await _fs.WriteAllBytes(exePath, exeContent).ConfigureAwait(false);

        var manifest = new UpdateManifest {
            LatestVersion = version,
            Channel = "stable",
            Releases = [
                new UpdateManifestEntry {
                    Version = version,
                    DownloadUrl = $"releases/{version}/jcc.exe",
                    Sha256 = sha256,
                    SizeBytes = exeContent.Length,
                    ReleaseNotes = $"测试版本 {version}",
                    PublishedAt = DateTimeOffset.UtcNow
                }
            ]
        };
        var manifestJson = JsonSerializer.Serialize(manifest, UpdateServerJsonContext.Default.UpdateManifest);
        await _fs.WriteAllText(_fs.CombinePath(_contentRoot, "manifest.json"), manifestJson).ConfigureAwait(false);
    }
}

/// <summary>更新服务器 JSON 序列化上下文 — AOT 友好</summary>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(UpdateManifest))]
internal sealed partial class UpdateServerJsonContext : JsonSerializerContext;