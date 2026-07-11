using JoinCode.Abstractions.Attributes;

namespace Core.Prompts.Sections;

[PromptSection(
    Name = "replacement_methodology",
    Keywords = new[] { "替换", "批量替换", "全局命名空间", "using迁移", "AST替换", "机械化替换" },
    InjectOn = PromptSectionInject.Keyword,
    Order = 62)]
public static class ReplacementMethodologySection
{
    public static string GetContent()
    {
        return """
# 替换方法论

当用户要求批量替换代码时，必须按以下方法论执行：

## 替换技巧

执行任何批量替换时，必须遵循以下技巧：

1. **每次替换前必须 git 备份**：执行替换脚本前先 `git add -A; git commit -m "备份: 替换前的快照"`，确保可随时回滚
2. **用脚本替换**：优先使用 CSX 脚本（C# 脚本），语法更强大、类型安全，比 PowerShell/Sed 更适合处理 C# 代码的替换逻辑
3. **替换后必须 diff 检查**：执行 `git --no-pager diff` 逐项检查替换结果，发现错误则手工修正或修改脚本后重新执行，避免下次替换重复相同错误
4. **按项目颗粒度渐进式**：每次只替换一个项目，替换后只需编译被替换的项目验证，而非全量编译
5. **最后再 git 提交**：确认该项目替换无误后，执行 `git add -A; git commit -m "替换: 描述"` 提交，再进入下一个项目

## AST替换

当需要替换函数签名或异步节点时：

1. **每次替换前必须 git 备份**：执行替换脚本前先 `git add -A; git commit -m "备份: 替换前的快照"`，确保可随时回滚
2. 先 AST工具 替换函数名（影响声明和所有调用点）
3. 再 AST工具 替换异步节点（async/await模式变更）
4. 反之亦可，但不能同时替换两种模式
5. git diff 检查替换结果,主要查看替换范围,是否跨项目了?可以 git 剔除多余的替换,因为我们已经备份.
6. git 提交.

可用MCP工具辅助：
- `code_index_find_references`: 查找符号的所有引用（定位所有需要替换的位置）
- `code_index_get_callers` / `code_index_get_callees`: 查找调用关系（确定替换影响范围）
- `code_index_get_impact_scope`: 分析修改指定符号的影响范围
- `lsp_find_references`: 通过LSP查找引用
- `lsp_workspace_symbol`: 在工作区中搜索符号

## 全局命名空间迁移

按项目粒度将 .cs 文件中的 using 命名空间替换到 GlobalUsings.cs：

1. 脚本提取所有项目中的 using 命名空间
2. 手动处理命名空间冲突
3. 手动处理意外情况（命名冲突、别名等）
4. 提交git
5. 渐进式实现，不要一次替换所有项目

这样每个项目内的别名就可以在全局中统一了。
""";
    }

    public static SystemPromptSection Create()
    {
        return SystemPromptSection.Cached("replacement_methodology", GetContent);
    }
}
