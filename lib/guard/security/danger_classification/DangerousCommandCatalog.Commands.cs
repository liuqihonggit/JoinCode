namespace Core.Security.DangerClassification;

/// <summary>
/// 命令映射表构建 — 委托给源码生成器扫描 [DangerCommand] 特性生成字典
/// 5级分级: Safe(白灯只读自动通过) / Unknown(黄灯未知命令ask) / LightValidation(绿灯可撤回ask) / Execution(红灯不可撤回ask) / Dangerous(黑灯直接拒绝)
/// 命令定义在 <see cref="DangerCommandDefinitions"/> 中,用 [DangerCommand] 特性标注
/// </summary>
public static partial class DangerousCommandCatalog
{
    private static FrozenDictionary<string, CommandEntry> BuildCommands() => BuildGeneratedCommands();
}
