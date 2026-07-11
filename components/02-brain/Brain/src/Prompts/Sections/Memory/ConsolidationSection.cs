using JoinCode.Abstractions.Attributes;

namespace Core.Prompts.Sections;

[PromptSection(
    Name = "consolidation",
    Keywords = new[] { "归纳", "合并", "整理", "整合", "统一", "合并到", "归类", "收拢" },
    InjectOn = PromptSectionInject.Keyword,
    Order = 63)]
public static class ConsolidationSection
{
    public static string GetContent()
    {
        return """
# 修改代码前必做

本次修改进行为了下次，能归纳就归纳到一个类就归纳，避免下次修改又要改那么多地方，不留技术债。

默认执行顺序是：先读取相关文件、引用关系，再移除旧的项（函数/类），修改测试，然后执行TDD，否则容易造成遗留旧项没有删干净。
""";
    }

    public static SystemPromptSection Create()
    {
        return SystemPromptSection.Cached("consolidation", GetContent);
    }
}
