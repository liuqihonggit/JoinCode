namespace JoinCode.Gui.WebHost;

/// <summary>
/// DSH 宿主服务容器（ADR 0143）：聚合前端宿主协议背后的状态与能力，由 <see cref="DshWebHost"/> 创建、
/// 注入 <see cref="DshRpcDispatcher"/> 与流式基线。M2 起把数据来源逐个换成本项目自有引擎，
/// 前端只认这些宿主能力，不感知实现。
/// </summary>
internal sealed class DshHostServices {
    /// <summary>构造宿主服务</summary>
    /// <param name="fileSystem">文件系统抽象（工作区注册表等持久化）</param>
    /// <param name="workspaceFilePath">工作区注册表路径（默认 <c>~/.jcc/dsh-workspaces.json</c>）</param>
    internal DshHostServices(IFileSystem fileSystem, string? workspaceFilePath = null) {
        Workspaces = new DshWorkspaceStore(fileSystem, workspaceFilePath);
        Settings = new DshSettingsStore();
    }

    /// <summary>工作区注册表（workspace/* 与 workspace/follow 基线）</summary>
    internal DshWorkspaceStore Workspaces { get; }

    /// <summary>设置命名空间（settings/describe 与 settings/mutate）</summary>
    internal DshSettingsStore Settings { get; }
}
