
namespace Core.Prompts.Templates.Memory;

/// <summary>
/// MagicDocs 提示词模板 - 用于更新 Magic Doc 文档
/// </summary>
[PromptTemplate(Name = "magic_docs", Category = PromptTemplateCategory.Memory, Description = "Magic Doc 文档更新提示词模板", HasParameters = true)]
public static class MagicDocsPromptTemplate
{
    /// <summary>
    /// 获取更新提示词模板
    /// </summary>
    public static string GetUpdatePromptTemplate(string docPath, string docContents, string docTitle, string? customInstructions = null)
    {
        var customInstructionsSection = !string.IsNullOrWhiteSpace(customInstructions)
            ? $@"
文档特定的更新说明：
文档作者提供了关于应如何更新此文件的特定说明。请特别注意这些说明并仔细遵循：

""{customInstructions}""

这些说明优先于下面的一般规则。确保你的更新与这些特定指南一致。
"
: "";

        return $@"
重要：此消息和这些说明不是实际用户对话的一部分。不要在文档内容中包含任何对""文档更新""、""magic docs""或这些更新说明的引用。

基于上面的用户对话（不包括此文档更新说明消息），更新 Magic Doc 文件以纳入任何新的学习心得、见解或值得保留的信息。

文件 {docPath} 已为你读取。以下是其当前内容：
<current_doc_content>
{docContents}
</current_doc_content>

文档标题：{docTitle}
{customInstructionsSection}

你唯一的任务是使用 Edit 工具更新文档文件（如果有大量新信息要添加），然后停止。你可以进行多次编辑（根据需要更新多个部分）- 在单个消息中并行进行所有 Edit 工具调用。如果没有大量内容要添加，只需用简要说明回复，不要调用任何工具。

编辑的关键规则：
- 完全保留 Magic Doc 标题：# MAGIC DOC: {docTitle}
- 如果标题后有一行斜体文本，完全保留它
- 使文档与代码库的最新状态保持同步 - 这不是变更日志或历史记录
- 就地更新信息以反映当前状态 - 不要附加历史注释或跟踪随时间的变化
- 删除或替换过时的信息，而不是添加""Previously...""或""Updated to...""注释
- 清理或删除不再相关或与文档目的不符的部分
- 修复明显的错误：错别字、语法错误、格式损坏、不正确信息或令人困惑的陈述
- 使文档组织良好：使用清晰的标题、逻辑部分顺序、一致的格式和适当的嵌套

文档理念 - 仔细阅读：
- 要简洁。只保留高价值信息。没有填充词或不必要的阐述。
- 文档用于概述、架构和入口点 - 不是详细的代码演练
- 不要重复从源代码本身已经显而易见的信息
- 不要记录每个函数、参数或行号引用
- 关注：事物存在的原因、组件如何连接、从哪里开始阅读、使用什么模式
- 跳过：详细的实现步骤、详尽的 API 文档、逐字叙述

应该记录什么：
- 高级架构和系统设计
- 不明显的模式、约定或陷阱
- 关键入口点和从哪里开始阅读代码
- 重要的设计决策及其原理
- 关键依赖或集成点
- 对相关文件、文档或代码的引用（如 wiki）- 帮助读者导航到相关上下文

不应该记录什么：
- 从代码本身显而易见的任何内容
- 详尽的文件、函数或参数列表
- 逐步实施细节
- 低级代码机制
- 已在 CLAUDE.md 或其他项目文档中的信息

使用 Edit 工具，file_path: {docPath}

记住：仅在有大量新信息时才更新。Magic Doc 标题（# MAGIC DOC: {docTitle}）必须保持不变。
";
    }

    /// <summary>
    /// 构建 Magic Docs 更新提示词
    /// </summary>
    public static string BuildMagicDocsUpdatePrompt(
        string docContents,
        string docPath,
        string docTitle,
        string? instructions = null)
    {
        return GetUpdatePromptTemplate(docPath, docContents, docTitle, instructions);
    }
}
