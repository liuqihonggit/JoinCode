namespace Core.Agents.Coordinator;

/// <summary>
/// 进程内面板后端 — 不创建真实终端面板，所有操作为空实现，用于无终端环境下的占位
/// </summary>
[Register(typeof(JoinCode.Abstractions.Interfaces.IPaneBackend), ServiceLifetime.Singleton)]
public sealed partial class InProcessPaneBackend : ServiceEntity, JoinCode.Abstractions.Interfaces.IPaneBackend
{
    /// <summary>后端类型标识，固定为 InProcess</summary>
    public JoinCode.Abstractions.Interfaces.BackendType BackendType => JoinCode.Abstractions.Interfaces.BackendType.InProcess;

    /// <summary>
    /// 为队友创建一个进程内占位面板
    /// </summary>
    /// <param name="teammateId">队友标识，直接作为面板 ID</param>
    /// <param name="command">面板启动命令（占位实现忽略此参数）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>面板创建结果，面板 ID 等于队友 ID</returns>
    public Task<JoinCode.Abstractions.Interfaces.CreatePaneResult> CreateTeammatePaneAsync(string teammateId, string command, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new JoinCode.Abstractions.Interfaces.CreatePaneResult
        {
            PaneId = teammateId,
            BackendType = JoinCode.Abstractions.Interfaces.BackendType.InProcess
        });
    }

    /// <summary>
    /// 向指定面板发送命令（占位实现，不执行任何操作）
    /// </summary>
    /// <param name="paneId">目标面板 ID</param>
    /// <param name="command">要发送的命令文本</param>
    /// <param name="cancellationToken">取消令牌</param>
    public Task SendCommandToPaneAsync(string paneId, string command, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// 设置面板边框颜色（占位实现，不执行任何操作）
    /// </summary>
    /// <param name="paneId">目标面板 ID</param>
    /// <param name="colorHex">十六进制颜色值</param>
    /// <param name="cancellationToken">取消令牌</param>
    public Task SetPaneBorderColorAsync(string paneId, string colorHex, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// 设置面板标题（占位实现，不执行任何操作）
    /// </summary>
    /// <param name="paneId">目标面板 ID</param>
    /// <param name="title">面板标题文本</param>
    /// <param name="cancellationToken">取消令牌</param>
    public Task SetPaneTitleAsync(string paneId, string title, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// 关闭指定面板（占位实现，不执行任何操作）
    /// </summary>
    /// <param name="paneId">目标面板 ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    public Task KillPaneAsync(string paneId, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// 重新平衡面板布局（占位实现，不执行任何操作）
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    public Task RebalancePanesAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
