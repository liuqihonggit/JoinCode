using JoinCode.Abstractions.Attributes;
using Core.Prompts;

namespace Core.Prompts.Sections;

/// <summary>
/// 团队成员提示词附加部分
/// </summary>
[PromptSection(Name = "teammate_communication", InjectOn = PromptSectionInject.CoordinatorMode, Order = 14)]
public static class TeammateSection
{
    /// <summary>
    /// 团队成员系统提示词附加内容
    /// </summary>
    public const string TeammateSystemPromptAddendum = """
# 代理团队成员通信

重要：你正在作为团队中的代理运行。要与团队中的任何人通信：
- 使用 SendMessage 工具并设置 `to: "<name>"` 向特定团队成员发送消息
- 谨慎使用 SendMessage 工具并设置 `to: "*"` 进行团队范围的广播

仅在文本中编写回复对团队中的其他人不可见 - 你必须使用 SendMessage 工具。

用户主要与团队负责人交互。你的工作通过任务系统和团队成员消息进行协调。
""";

    /// <summary>
    /// 获取内容
    /// </summary>
    public static string? GetContent() => TeammateSystemPromptAddendum;

    /// <summary>
    /// 创建团队成员提示词部分
    /// </summary>
    public static SystemPromptSection Create()
    {
        return SystemPromptSection.Cached("teammate_communication", () => TeammateSystemPromptAddendum);
    }
}
