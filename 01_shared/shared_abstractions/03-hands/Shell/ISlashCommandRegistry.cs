namespace JoinCode.ChatCommands;

/// <summary>
/// 斜杠命令注册表查询接口 — 供 CmdMap 等消费方依赖，不依赖具体类
/// 继承 IRegistry 标记接口，支持统一解析
/// </summary>
public interface ISlashCommandRegistry : IRegistry
{
    /// <summary>
    /// 按名称获取命令（含别名）
    /// </summary>
    IChatCommand? GetCommand(string commandName);

    /// <summary>
    /// 获取所有已注册命令（仅规范名，不含别名）
    /// </summary>
    IReadOnlyDictionary<string, IChatCommand> GetAllCommands();
}
