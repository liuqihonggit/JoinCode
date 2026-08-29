namespace Core.Prompts.Sections;

/// <summary>
/// 用户委托模式 — 当用户表示离开/委托时注入，授权 Agent 自主决策不再追问
/// 关键词触发：睡觉/离开/你看着办/不要问了/你直接做完/你全权负责
/// </summary>
[PromptSection(Name = "user_delegation", Keywords = new[] { "睡觉", "离开", "走了", "看着办", "不要问", "别问", "直接做", "全权", "不用等", "自己处理", "回来再", "先忙", "忙去了", "sleep", "away", "you decide", "交给你", "你决定" }, InjectOn = PromptSectionInject.Keyword, Order = 83)]
public static class UserDelegationSection
{
    /// <summary>
    /// 用户委托自主决策规则提示词
    /// </summary>
    public static string GetContent()
    {
        return """
# 用户委托自主决策模式

用户已表示离开或委托你全权处理。切换为自主决策模式：

## 核心规则
1. 不再向用户发起任何追问，基于现有信息自主决策
2. 缺失信息使用合理占位符或推断填充，并在结果中标注哪些是推断的
3. 优先保证任务完成度，而非信息完美度
4. 遇到技术决策分叉时，选择更保守/安全的方案

## 输出标注
- 推断的信息用【待确认】标记
- 缺失的数据用【需补充】标记
- 在结果末尾列出所有推断项和缺失项，方便用户回来后快速补充

## 禁止行为
- 禁止因信息不足而停止执行
- 禁止再次追问用户
- 禁止等待用户确认后再继续
""";
    }

    /// <summary>
    /// 创建用户委托提示词部分
    /// </summary>
    public static SystemPromptSection Create() =>
        SystemPromptSection.Cached("user_delegation", GetContent);
}
