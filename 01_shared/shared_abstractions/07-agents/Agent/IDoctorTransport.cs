namespace JoinCode.Abstractions.Interfaces.Doctor;

/// <summary>
/// 医生 IPC 传输接口 — 从病人进程读取遥测事件，向病人发送指令
/// 支持多种传输实现：SSE（多病人）、stdio（单病人）
/// </summary>
public interface IDoctorTransport : IAsyncDisposable
{
    /// <summary>
    /// 连接到病人进程的遥测通道
    /// </summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取下一条诊断事件（阻塞直到有数据或连接断开）
    /// </summary>
    /// <returns>诊断事件，连接断开时返回 null</returns>
    Task<DiagnosticEvent?> ReadEventAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 向指定病人进程发送指令
    /// </summary>
    /// <param name="patientId">目标病人 ID</param>
    /// <param name="command">指令内容</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task SendCommandAsync(string patientId, string command, CancellationToken cancellationToken = default);

    /// <summary>
    /// 向所有病人广播指令
    /// </summary>
    Task BroadcastCommandAsync(string command, CancellationToken cancellationToken = default);

    /// <summary>
    /// 是否已连接
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// 已连接的病人 ID 列表
    /// </summary>
    IReadOnlyList<string> ConnectedPatientIds { get; }

    /// <summary>
    /// 诊断事件接收事件 — 当从病人遥测中解析出事件时触发
    /// </summary>
    event EventHandler<DiagnosticEvent>? EventReceived;

    /// <summary>
    /// 病人连接事件 — 新病人连接时触发
    /// </summary>
    event EventHandler<string>? PatientConnected;

    /// <summary>
    /// 病人断开事件 — 病人断开连接时触发
    /// </summary>
    event EventHandler<string>? PatientDisconnected;
}
