namespace JoinCode.Abstractions.Prompts.ToolPrompts;

/// <summary>
/// EnterPlanModeTool 提示词
/// </summary>
[ToolPrompt(ToolName = "EnterPlanMode", Category = ToolPromptCategory.Planning)]
public static class EnterPlanModeToolPrompt
{
    public const string ToolName = PlanToolNameConstants.EnterPlanMode;

    /// <summary>
    /// 获取外部用户提示词
    /// </summary>
    public static string GetExternalPrompt(string askUserQuestionToolName, bool interviewPhaseEnabled = false)
    {
        var whatHappensSection = interviewPhaseEnabled
            ? ""
            : $@"## 计划模式中会发生什么

在计划模式中，你将：
1. 使用 Glob、Grep 和 Read 工具彻底探索代码库
2. 理解现有模式和架构
3. 设计实现方法
4. 向用户展示你的计划以获得批准
5. 如果需要，使用 {askUserQuestionToolName} 澄清方法
6. 准备好实施时使用 ExitPlanMode 退出计划模式

";

        return $@"当你即将开始一个非平凡的实现任务时，主动使用此工具。在编写代码之前获得用户对你方法的认可可以防止浪费精力并确保一致性。此工具将你过渡到计划模式，在那里你可以探索代码库并设计实现方法供用户批准。

## 何时使用此工具

**对于实现任务，除非它们很简单，否则优先使用 EnterPlanMode**。当以下任何条件适用时使用它：

1. **新功能实现**：添加有意义的新功能
   - 示例：""添加一个注销按钮"" - 它应该放在哪里？点击时应该发生什么？
   - 示例：""添加表单验证"" - 什么规则？什么错误消息？

2. **多种有效方法**：任务可以用几种不同的方式解决
   - 示例：""向 API 添加缓存"" - 可以使用 Redis、内存、基于文件的等
   - 示例：""提高性能"" - 许多优化策略可能

3. **代码修改**：影响现有行为或结构的更改
   - 示例：""更新登录流程"" - 究竟应该改变什么？
   - 示例：""重构此组件"" - 目标架构是什么？

4. **架构决策**：任务需要在模式或技术之间选择
   - 示例：""添加实时更新"" - WebSockets vs SSE vs 轮询
   - 示例：""实现状态管理"" - Redux vs Context vs 自定义解决方案

5. **多文件更改**：任务可能会触及超过 2-3 个文件
   - 示例：""重构认证系统""
   - 示例：""添加带有测试的新 API 端点""

6. **不明确的要求**：你需要在理解完整范围之前进行探索
   - 示例：""让应用更快"" - 需要分析并识别瓶颈
   - 示例：""修复结账中的错误"" - 需要调查根本原因

7. **用户偏好很重要**：实现可以合理地以多种方式进行
   - 如果你会使用 {askUserQuestionToolName} 来澄清方法，请改用 EnterPlanMode
   - 计划模式让你先探索，然后展示带有上下文的选项

## 何时不使用此工具

仅对简单任务跳过 EnterPlanMode：
- 单行或几行修复（拼写错误、明显错误、小调整）
- 添加具有明确要求的一个函数
- 用户给出了非常具体、详细说明的任务
- 纯研究/探索任务（改用 Agent 工具并选择 explore 代理）

{whatHappensSection}## 示例

### 好的 - 使用 EnterPlanMode：
用户：""向应用添加用户认证""
- 需要架构决策（session vs JWT，在哪里存储 token，中间件结构）

用户：""优化数据库查询""
- 多种方法可能，需要首先分析，影响重大

用户：""实现深色模式""
- 关于主题系统的架构决策，影响许多组件

用户：""向用户资料添加删除按钮""
- 看起来简单但涉及：放在哪里，确认对话框，API 调用，错误处理，状态更新

用户：""更新 API 中的错误处理""
- 影响多个文件，用户应该批准方法

### 不好的 - 不要使用 EnterPlanMode：
用户：""修复 README 中的拼写错误""
- 直接，不需要规划

用户：""向此函数添加 console.log 以进行调试""
- 简单，明显的实现

用户：""哪些文件处理路由？""
- 研究任务，不是实现规划

## 重要说明

- 此工具需要用户批准 - 他们必须同意进入计划模式
- 如果不确定是否使用它，倾向于规划 - upfront 获得一致性比重做工作更好
- 用户喜欢在对其代码库进行重大更改之前被咨询
";
    }

    /// <summary>
    /// 获取 Ant 内部用户提示词
    /// </summary>
    public static string GetAntPrompt(string askUserQuestionToolName, bool interviewPhaseEnabled = false)
    {
        var whatHappensSection = interviewPhaseEnabled
            ? ""
            : $@"## 计划模式中会发生什么

在计划模式中，你将：
1. 使用 Glob、Grep 和 Read 工具彻底探索代码库
2. 理解现有模式和架构
3. 设计实现方法
4. 向用户展示你的计划以获得批准
5. 如果需要，使用 {askUserQuestionToolName} 澄清方法
6. 准备好实施时使用 ExitPlanMode 退出计划模式

";

        return $@"当任务对正确的方法有真正的模糊性，并且在编码之前获得用户输入可以防止重大返工时，使用此工具。此工具将你过渡到计划模式，在那里你可以探索代码库并设计实现方法供用户批准。

## 何时使用此工具

当实现方法真正不清楚时，计划模式很有价值。在以下情况下使用它：

1. **重大架构模糊性**：存在多种合理的方法，选择对代码库有有意义的影响
   - 示例：""向 API 添加缓存"" - Redis vs 内存 vs 基于文件的
   - 示例：""添加实时更新"" - WebSockets vs SSE vs 轮询

2. **不明确的要求**：你需要在能够取得进展之前探索和澄清
   - 示例：""让应用更快"" - 需要分析并识别瓶颈
   - 示例：""重构此模块"" - 需要了解目标架构应该是什么

3. **高影响重构**：任务将显著重组现有代码，首先获得支持可以降低风险
   - 示例：""重新设计认证系统""
   - 示例：""从一种状态管理方法迁移到另一种""

## 何时不使用此工具

当你可以合理推断正确的方法时，跳过计划模式：
- 即使触及多个文件，任务也是直接的
- 用户的请求足够具体，实现路径是明确的
- 你正在添加具有明显实现模式的功能（例如，添加按钮、遵循现有约定的新端点）
- 一旦理解错误，修复就很清楚的错误修复
- 研究/探索任务（改用 Agent 工具）
- 用户说类似""我们能做 X 吗""或""让我们做 X""的话 —— 直接开始

如有疑问，优先开始工作并使用 {askUserQuestionToolName} 提出具体问题，而不是进入完整的规划阶段。

{whatHappensSection}## 示例

### 好的 - 使用 EnterPlanMode：
用户：""向应用添加用户认证""
- 真正模糊：session vs JWT，在哪里存储 token，中间件结构

用户：""重新设计数据管道""
- 重大重组，错误的方法会浪费大量精力

### 不好的 - 不要使用 EnterPlanMode：
用户：""向用户资料添加删除按钮""
- 实现路径明确；直接做

用户：""我们能做搜索功能吗？""
- 用户想开始，不是规划

用户：""更新 API 中的错误处理""
- 开始工作；如果需要，问具体问题

用户：""修复 README 中的拼写错误""
- 直接，不需要规划

## 重要说明

- 此工具需要用户批准 - 他们必须同意进入计划模式
";
    }
}
