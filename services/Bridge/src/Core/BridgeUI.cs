
namespace Core.Bridge;

/// <summary>
/// QR 码数据 - 用于终端扫码连接 Bridge 会话
/// </summary>
public sealed partial class BridgeQRCodeData
{
    [JsonPropertyName("sessionId")]
    public required string SessionId { get; init; }

    [JsonPropertyName("endpoint")]
    public required string Endpoint { get; init; }

    [JsonPropertyName("token")]
    public required string Token { get; init; }

    [JsonPropertyName("expiresAt")]
    public required long ExpiresAt { get; init; }
}

/// <summary>
/// 会话显示信息 - 用于终端展示活跃会话列表
/// </summary>
public sealed partial class BridgeSessionDisplay
{
    [JsonPropertyName("sessionId")]
    public required string SessionId { get; init; }

    [JsonPropertyName("clientName")]
    public string? ClientName { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("connectedAt")]
    public required long ConnectedAt { get; init; }
}

/// <summary>
/// Bridge UI 服务 - 生成 QR 码数据和会话列表展示
/// 注意: 不得依赖 BridgeServer,否则会形成 DI 循环依赖 (BridgeServer → BridgeServerSession → BridgeUIService → BridgeServer)
/// </summary>
[Register]
public sealed partial class BridgeUIService
{
    [Inject] private readonly ILogger<BridgeUIService>? _logger;
    private readonly IClockService _clock;
    private readonly ConcurrentDictionary<string, BridgeSessionDisplay> _activeSessions;

    public BridgeUIService(
        ILogger<BridgeUIService>? logger = null,
        IClockService? clock = null)
    {
        _logger = logger;
        _clock = clock ?? SystemClockService.Instance;
        _activeSessions = new ConcurrentDictionary<string, BridgeSessionDisplay>();
    }

    /// <summary>
    /// 生成 QR 码数据 - 包含会话端点和认证令牌
    /// </summary>
    /// <param name="sessionId">会话 ID</param>
    /// <param name="endpoint">连接端点</param>
    /// <param name="ttlMs">令牌有效期（毫秒）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>QR 码数据</returns>
    public Task<BridgeQRCodeData> GenerateQRDataAsync(
        string sessionId,
        string endpoint,
        int ttlMs = WorkflowConstants.Bridge.DefaultQRTokenTtlMs,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(endpoint);

        var token = GenerateAuthToken();
        var expiresAt = _clock.GetUtcNowOffset().AddMilliseconds(ttlMs).ToUnixTimeMilliseconds();

        var qrData = new BridgeQRCodeData
        {
            SessionId = sessionId,
            Endpoint = endpoint,
            Token = token,
            ExpiresAt = expiresAt
        };

        _logger?.LogDebug("[BridgeUIService] 已生成 QR 数据，会话: {SessionId}", sessionId);

        return Task.FromResult(qrData);
    }

    /// <summary>
    /// 将 QR 码数据格式化为终端可显示的字符串
    /// 使用 QRCoder 库生成真实 QR 矩阵，以 UTF-8 块字符渲染
    /// 对齐 TS 端 bridgeUI.ts 的 generateQr 实现
    /// </summary>
    /// <param name="qrData">QR 码数据</param>
    /// <returns>终端展示字符串</returns>
    public string FormatAsTerminalQR(BridgeQRCodeData qrData)
    {
        ArgumentNullException.ThrowIfNull(qrData);

        var expiresTime = DateTimeOffset.FromUnixTimeMilliseconds(qrData.ExpiresAt)
            .ToLocalTime()
            .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        // 构建 QR 码编码的 URL 内容
        var qrContent = $"{qrData.Endpoint}?session={qrData.SessionId}&token={qrData.Token}";

        // 使用 QRCoder 生成 QR 矩阵
        using var qrGenerator = new QRCodeGenerator();
        var qrCodeData = qrGenerator.CreateQrCode(qrContent, QRCodeGenerator.ECCLevel.L);

        // 渲染为 UTF-8 块字符终端显示（对齐 TS 端 qrcode 库的 type: 'utf8', small: true）
        var qrLines = RenderUtf8BlockQR(qrCodeData);

        var sb = new StringBuilder();
        sb.AppendLine("┌─────────────────────────────┐");
        sb.AppendLine("│       Bridge QR Code        │");
        sb.AppendLine("│  ┌───────────────────────┐  │");

        foreach (var line in qrLines)
        {
            sb.AppendLine($"│  │ {line,-23} │  │");
        }

        sb.AppendLine("│  └───────────────────────┘  │");
        sb.AppendLine("│                             │");
        sb.AppendLine($"│  Session: {qrData.SessionId[..Math.Min(qrData.SessionId.Length, 16)]}  │");
        sb.AppendLine($"│  Expires: {expiresTime}  │");
        sb.AppendLine("└─────────────────────────────┘");

        return sb.ToString();
    }

    /// <summary>
    /// 将 QR 码矩阵渲染为 UTF-8 块字符行
    /// 使用 ▀ █ ▄ 字符实现每两个模块行合并为一行输出，提高终端密度
    /// 对齐 TS 端 qrcode 库的 type: 'utf8', small: true 渲染模式
    /// </summary>
    private static List<string> RenderUtf8BlockQR(QRCodeData qrCodeData)
    {
        var moduleCount = qrCodeData.ModuleMatrix.Count;
        var lines = new List<string>();

        // 每两行模块合并为一行输出字符（上黑下白=▀，全黑=█，上白下黑=▄，全白=空格）
        for (var row = 0; row < moduleCount; row += 2)
        {
            var sb = new StringBuilder(moduleCount);
            for (var col = 0; col < moduleCount; col++)
            {
                var topDark = qrCodeData.ModuleMatrix[row][col];
                var bottomDark = row + 1 < moduleCount && qrCodeData.ModuleMatrix[row + 1][col];

                sb.Append((topDark, bottomDark) switch
                {
                    (true, true) => '█',
                    (true, false) => '▀',
                    (false, true) => '▄',
                    (false, false) => ' '
                });
            }

            lines.Add(sb.ToString());
        }

        return lines;
    }

    /// <summary>
    /// 获取活跃会话列表 - 用于终端展示
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>活跃会话显示列表</returns>
    public Task<IReadOnlyList<BridgeSessionDisplay>> GetActiveSessionList(CancellationToken ct = default)
    {
        var sessions = _activeSessions.Values
            .OrderByDescending(s => s.ConnectedAt)
            .ToList();

        _logger?.LogDebug("[BridgeUIService] 获取活跃会话列表，数量: {Count}", sessions.Count);

        return Task.FromResult<IReadOnlyList<BridgeSessionDisplay>>(sessions);
    }

    /// <summary>
    /// 注册会话到活跃列表
    /// </summary>
    /// <param name="session">会话显示信息</param>
    public void RegisterSession(BridgeSessionDisplay session)
    {
        ArgumentNullException.ThrowIfNull(session);

        _activeSessions[session.SessionId] = session;
        _logger?.LogDebug("[BridgeUIService] 注册会话: {SessionId}", session.SessionId);
    }

    /// <summary>
    /// 从活跃列表移除会话
    /// </summary>
    /// <param name="sessionId">会话 ID</param>
    public void UnregisterSession(string sessionId)
    {
        if (_activeSessions.TryRemove(sessionId, out _))
        {
            _logger?.LogDebug("[BridgeUIService] 移除会话: {SessionId}", sessionId);
        }
    }

    /// <summary>
    /// 生成认证令牌 - 使用加密安全的随机字节
    /// </summary>
    private static string GenerateAuthToken()
    {
        return RandomNumberGenerator.GetHexString(32, lowercase: false);
    }
}
