namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 错误消息常量
/// </summary>
public static class ErrorMessages
{
    /// <summary>
    /// 文件不存在
    /// </summary>
    public const string FileNotFound = "文件不存在";

    /// <summary>
    /// NullQueryEngine 不支持聊天完成
    /// </summary>
    public const string NullQueryEngineNotSupportChat = "NullQueryEngine does not support chat completion";

    /// <summary>
    /// NullQueryEngine 没有 Kernel
    /// </summary>
    public const string NullQueryEngineNoKernel = "NullQueryEngine does not have a Kernel";

    /// <summary>
    /// 当前不在计划模式
    /// </summary>
    public const string NotInPlanMode = "当前不在计划模式";

    /// <summary>
    /// 用户取消退出
    /// </summary>
    public const string UserCancelledExit = "用户取消退出";

    /// <summary>
    /// 工具步骤必须指定 Tool
    /// </summary>
    public const string ToolStepMustSpecifyTool = "工具步骤必须指定 Tool";

    /// <summary>
    /// 提示步骤必须指定 Prompt
    /// </summary>
    public const string PromptStepMustSpecifyPrompt = "提示步骤必须指定 Prompt";

    /// <summary>
    /// 循环步骤必须指定 Loop 配置
    /// </summary>
    public const string LoopStepMustSpecifyLoopConfig = "循环步骤必须指定 Loop 配置";

    /// <summary>
    /// 条件步骤必须指定 Condition
    /// </summary>
    public const string ConditionStepMustSpecifyCondition = "条件步骤必须指定 Condition";

    /// <summary>
    /// 技能不存在
    /// </summary>
    public const string SkillNotFound = "技能不存在: {0}";

    /// <summary>
    /// 步骤执行失败
    /// </summary>
    public const string StepExecutionFailed = "步骤执行失败";
}
