using JoinCode.Abstractions.Attributes;

namespace Core.Prompts.Sections;

/// <summary>
/// 事实追问模式 — 当用户发起任务型请求时注入，引导 Agent 主动追问缺失信息
/// 关键词触发：帮/写/分析/总结/规划/设计/生成/制作/优化/重构/修复/实现
/// </summary>
[PromptSection(Name = "fact_inquiry", Keywords = new[] { "写一", "做一", "分析", "总结", "规划", "设计方案", "生成", "制作", "周报", "报告", "文档", "方案", "整理", "梳理" }, InjectOn = PromptSectionInject.Keyword, Order = 82)]
public static class FactInquirySection
{
    /// <summary>
    /// 事实追问规则提示词
    /// </summary>
    public static string GetContent()
    {
        return """
# 事实完整性原则

你正在处理一项任务型请求。在执行前，必须确保关键信息充足：

## 追问规则
1. 识别用户请求中缺失的关键业务信息（目标、范围、约束、偏好）
2. 信息不足时，使用 ask_user 工具追问，每次只问一个问题
3. 追问时提供推荐选项，降低用户输入成本
4. 编码任务：优先自行通过工具（读文件、搜索代码）收集事实，仅在确实无法获取时才追问用户
5. 通用任务：信息不足必须追问，禁止猜测用户意图或编造业务数据

## 饱和度判断
- 用户提供了明确的目标+范围+约束 → 直接执行
- 用户提供了部分信息，缺少1-2个关键点 → 追问后再执行
- 用户请求极度模糊（如"帮我写个东西"） → 必须追问核心目标

## 禁止行为
- 禁止在信息不足时直接生成结果
- 禁止编造用户未提及的业务数据
- 禁止跳过追问直接猜测用户意图
""";
    }

    /// <summary>
    /// 创建事实追问提示词部分
    /// </summary>
    public static SystemPromptSection Create() =>
        SystemPromptSection.Cached("fact_inquiry", GetContent);
}
