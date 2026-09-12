namespace JoinCode.Abstractions.Security.Shell;

/// <summary>
/// 标记危险命令定义 — 源码生成器据此扫描生成 DangerousCommandCatalog.Commands 字典
/// </summary>
/// <remarks>
/// 标注在 DangerCommandDefinitions 的 const string 字段上,
/// 字段值为命令名(如 "ls"),特性参数指定风险类型/危险等级/描述。
/// 源码生成器扫描所有带此特性的字段,生成 FrozenDictionary&lt;string, CommandEntry&gt;。
/// </remarks>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class DangerCommandAttribute : Attribute
{
    /// <summary>
    /// 风险类型(消息构建辅助信息)
    /// </summary>
    public CommandRisk Risk { get; }

    /// <summary>
    /// 危险等级(权限决策唯一依据)
    /// </summary>
    public CommandDangerLevel Level { get; }

    /// <summary>
    /// 命令描述(人类可读,用于确认提示)
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// 初始化危险命令定义特性
    /// </summary>
    /// <param name="risk">风险类型</param>
    /// <param name="level">危险等级</param>
    /// <param name="description">命令描述</param>
    public DangerCommandAttribute(CommandRisk risk, CommandDangerLevel level, string description)
    {
        Risk = risk;
        Level = level;
        Description = description ?? throw new ArgumentNullException(nameof(description));
    }
}
