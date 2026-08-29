namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 命令注册钩子接口 - 允许插件通过钩子系统注册命令
/// <para>对齐 Cordis 可逆效应: RegisterCommands + UnregisterCommands 成对出现</para>
/// </summary>
public interface ICommandRegistrationHook
{
    /// <summary>
    /// 注册命令到命令注册中心
    /// </summary>
    void RegisterCommands(ICommandRegistry registry, IServiceProvider serviceProvider);

    /// <summary>
    /// 撤销命令注册 — 可逆效应,卸载时由 WorkflowPluginHost 自动调用
    /// </summary>
    void UnregisterCommands(ICommandRegistry registry);
}

/// <summary>
/// 命令注册中心接口 - 插件通过此接口注册命令，无需依赖 Core
/// </summary>
public interface ICommandRegistry : IRegistry
{
    /// <summary>
    /// 注册命令实例
    /// </summary>
    void Register(ICommand command);

    /// <summary>
    /// 取消注册命令
    /// </summary>
    bool UnregisterCommand(string commandName);
}
