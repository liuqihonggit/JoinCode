namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 命令注册钩子接口 - 允许插件通过钩子系统注册命令
/// </summary>
public interface ICommandRegistrationHook
{
    /// <summary>
    /// 注册命令到命令注册中心
    /// </summary>
    void RegisterCommands(ICommandRegistry registry, IServiceProvider serviceProvider);
}

/// <summary>
/// 命令注册中心接口 - 插件通过此接口注册命令，无需依赖 Core
/// </summary>
public interface ICommandRegistry
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
