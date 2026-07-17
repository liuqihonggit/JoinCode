namespace JoinCode.Abstractions.Interfaces.Doctor;

/// <summary>
/// 医生 IPC 传输接口 — 从病人进程读取遥测事件
/// </summary>
public interface IDoctorTransport : IAsyncDisposable
{
    /// <summary>
    /// 连接到病人进程的 stdout/stderr 流
    /// </summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取下一条诊断事件（阻塞直到有数据或连接断开）
    /// </summary>
    /// <returns>诊断事件，连接断开时返回 null</returns>
    Task<DiagnosticEvent?> ReadEventAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 向病人进程发送指令（通过 stdin）
    /// </summary>
    Task SendCommandAsync(string command, CancellationToken cancellationToken = default);

    /// <summary>
    /// 是否已连接
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// 诊断事件接收事件 — 当从病人 stdout 解析出事件时触发
    /// </summary>
    event EventHandler<DiagnosticEvent>? EventReceived;
}
