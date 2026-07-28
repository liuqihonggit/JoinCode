namespace JoinCode.Abstractions.ChatCommands;

/// <summary>
/// /memory 命令子操作集合。
/// 适用范围: /memory [edit|open|add|search|db|stats|health|cleanup]
/// 8 个 case 全部 Memory 专属,无与 ToggleAction/CrudAction 重叠的动作,
/// 创建独立枚举统一管理避免 8 处硬编码字符串扩散。
///
/// 使用示例:
/// - FromValue("edit")  → MemorySubCommand.Edit
/// - FromValue("STATS") → MemorySubCommand.Stats (OrdinalIgnoreCase)
/// - MemorySubCommand.Cleanup.ToValue() → "cleanup"
/// </summary>
public enum MemorySubCommand
{
    /// <summary>编辑记忆文件(交互式选择或指定路径)</summary>
    [EnumValue("edit")] Edit,

    /// <summary>在系统文件管理器中打开记忆目录</summary>
    [EnumValue("open")] Open,

    /// <summary>添加一条新记忆</summary>
    [EnumValue("add")] Add,

    /// <summary>按关键词搜索记忆</summary>
    [EnumValue("search")] Search,

    /// <summary>列出数据库中存储的记忆</summary>
    [EnumValue("db")] Db,

    /// <summary>显示记忆统计信息</summary>
    [EnumValue("stats")] Stats,

    /// <summary>显示记忆健康报告</summary>
    [EnumValue("health")] Health,

    /// <summary>清理过期/低价值记忆</summary>
    [EnumValue("cleanup")] Cleanup,
}
