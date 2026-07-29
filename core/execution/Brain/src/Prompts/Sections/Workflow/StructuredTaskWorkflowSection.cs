using JoinCode.Abstractions.Attributes;

namespace Core.Prompts.Sections;

[PromptSection(
    Name = "structured_task_workflow",
    Keywords = new[] { "修改", "实现", "重构", "修复", "添加", "更改", "变更", "改造" },
    InjectOn = PromptSectionInject.Keyword,
    Order = 64)]
public static class StructuredTaskWorkflowSection
{
    public static string GetContent()
    {
        return """
# 结构化任务工作流

当用户输入包含"修改"、"实现"、"重构"、"修复"、"添加"等任务关键词时，你必须按以下工作流执行：

## 第1步：结构化用户对话

1. 将用户对话结构化为有序任务列表，用序号表达任务的先后顺序——先容易再困难
2. 每个任务使用以下格式：
    - 任务序号
    - 任务描述
    - 结果（初始为空）
    - 可能性列表：
        - 可能性1：描述 → 排除?（初始未排除）
        - 可能性2：描述 → 排除?（初始未排除）
3. 将结构化任务存入 .jcc/task 目录（Markdown格式）
4. 逐个排除可能性，排除了什么必须记录到任务文档——否则用户不知道你到底排除了什么问题
5. 存入任务队列

## 第2步：检索代码

a. 先用 `code_index_search` 搜索可能性高的函数名称
b. 用 `code_index_get_callers` / `code_index_get_callees` 获取引用关系图
c. 用 `code_index_find_references` / `lsp_find_references` 并行提取代码片段
d. 用 `code_index_get_dependencies` 获取公开的API依赖，日后组织新代码可以使用
e. 根据用户对话，用 grep 搜索更多可能的代码
f. （可选）用 `code_index_get_impact_scope` 再次查找影响范围，直到满足修改需求
g. 用 `code_index_explore` 渐进式探索代码：从符号索引→调用关系→源代码

## 第3步：执行任务

a. 进行渐进式修改，每次TDD红绿循环，没有复现BUG就不允许修改
b. 每一个小任务都写入git提交，并且要把自主决策写入提交信息

## 第4步：检查遗留

完成任务后检查是否有遗留问题或未完成的工作

## 第5步：性能diff检查

每次完成任务之后执行diff，判断是否存在性能问题，如有就推荐用户下一步解决
""";
    }

    public static SystemPromptSection Create()
    {
        return SystemPromptSection.Cached("structured_task_workflow", GetContent);
    }
}
