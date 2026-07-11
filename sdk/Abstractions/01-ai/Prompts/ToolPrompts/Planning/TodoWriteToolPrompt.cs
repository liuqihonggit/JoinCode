namespace JoinCode.Abstractions.Prompts.ToolPrompts;

/// <summary>
/// TodoWriteTool 提示词
/// </summary>
[ToolPrompt(ToolName = "TodoWrite", Category = ToolPromptCategory.Planning)]
public static class TodoWriteToolPrompt
{
    public const string ToolName = TodoToolNameConstants.TodoWrite;

    public const string Prompt = """
        使用此工具为当前编码会话创建和管理结构化任务列表。这有助于你跟踪进度、组织复杂任务，并向用户展示彻底性。
        它还有助于用户理解任务进度和整体请求进度。

        ## 何时使用此工具
        在以下场景中主动使用此工具：

        1. 复杂多步骤任务 - 当任务需要 3 个或更多不同的步骤或操作时
        2. 非平凡和复杂任务 - 需要仔细规划或多个操作的任务
        3. 用户明确要求待办列表 - 当用户直接要求你使用待办列表时
        4. 用户提供多个任务 - 当用户提供要做的事情列表（编号或逗号分隔）时
        5. 收到新指令后 - 立即将用户需求捕获为待办事项
        6. 当你开始处理任务时 - 在开始工作之前将其标记为 in_progress。理想情况下，你应该一次只有一个待办事项处于 in_progress 状态
        7. 完成任务后 - 将其标记为 completed 并添加在实现过程中发现的任何新后续任务

        ## 何时不使用此工具

        在以下情况下跳过使用此工具：
        1. 只有一个简单直接的任务
        2. 任务微不足道，跟踪它没有组织上的好处
        3. 任务可以在少于 3 个微不足道的步骤中完成
        4. 任务纯粹是对话性或信息性的

        注意，如果只有一个微不足道的任务要做，你不应该使用此工具。在这种情况下，你最好直接完成任务。

        ## 使用待办列表的示例

        <example>
        User: 我想在应用程序设置中添加一个深色模式切换。确保你在完成后运行测试和构建！
        Assistant: *创建具有以下项目的待办列表：*
        1. 在设置页面中创建深色模式切换组件
        2. 添加深色模式状态管理（context/store）
        3. 实现深色主题的 CSS-in-JS 样式
        4. 更新现有组件以支持主题切换
        5. 运行测试和构建过程，解决出现的任何失败或错误
        *开始处理第一个任务*

        <reasoning>
        助手使用待办列表是因为：
        1. 添加深色模式是一个需要 UI、状态管理和样式更改的多步骤功能
        2. 用户明确要求之后运行测试和构建
        3. 助手推断测试和构建需要通过添加"确保测试和构建成功"作为最终任务来通过
        </reasoning>
        </example>

        <example>
        User: 帮我把项目中所有 getCwd 函数重命名为 getCurrentWorkingDirectory
        Assistant: *使用 grep 或搜索工具定位代码库中 getCwd 的所有实例*
        我在 8 个不同的文件中找到了 15 个 getCwd 实例。
        *为每个需要更新的文件创建具有特定项目的待办列表*

        <reasoning>
        助手使用待办列表是因为：
        1. 首先，助手搜索以了解任务的范围
        2. 在发现跨不同文件的多个出现后，它确定这是一个具有多个步骤的复杂任务
        3. 待办列表有助于确保每个实例都被跟踪并系统地更新
        4. 这种方法防止遗漏任何出现并保持代码一致性
        </reasoning>
        </example>


        <example>
        User: 我需要为我的电子商务网站实现这些功能：用户注册、产品目录、购物车和结账流程。
        Assistant: *根据项目架构将每个功能分解为特定任务创建待办列表*
        让我们从用户注册开始。这将涉及创建数据库模型、API 端点和与现有身份验证系统集成的前端表单。

        <reasoning>
        助手使用待办列表是因为：
        1. 用户在逗号分隔的列表中提供了多个复杂功能要实现
        2. 待办列表有助于将这些大功能组织成可管理的任务
        3. 这种方法允许在整个实现过程中跟踪进度
        </reasoning>
        </example>

        <example>
        User: 你能帮我优化我的 React 应用程序吗？它渲染很慢，有性能问题。
        Assistant: *审查组件结构、渲染模式、状态管理和数据获取*
        在分析你的代码库后，我发现了几个性能问题。
        *创建具有以下项目的待办列表：1) 在 ProductList 中实现昂贵计算的 memoization，2) 在 Dashboard 中为长列表添加虚拟化，3) 优化 Gallery 组件中的图像加载，4) 修复 ShoppingCart 中的状态更新循环，5) 审查包大小并实现代码分割*
        让我们从在 ProductList 组件中实现昂贵计算的 memoization 开始。

        <reasoning>
        助手使用待办列表是因为：
        1. 首先，助手检查代码库以识别特定的性能问题
        2. 基于此分析，它识别了多个优化机会
        3. 性能优化是一个需要多个步骤的非平凡任务
        4. 待办列表有助于系统地跟踪跨不同组件的改进
        5. 这种系统方法确保所有性能瓶颈都被解决
        </reasoning>
        </example>

        ## 不使用待办列表的示例

        <example>
        User: 如何在 Python 中打印 'Hello World'？
        Assistant: 在 Python 中，你可以使用这个简单的代码打印 "Hello World"：

        ```python
        print("Hello World")
        ```

        这将在执行时将 "Hello World" 输出到控制台。

        <reasoning>
        助手没有使用待办列表，因为这是一个可以在一个步骤中完成的单一、直接任务。没有必要跟踪这样的简单请求。
        </reasoning>
        </example>

        <example>
        User: git status 命令是做什么的？
        Assistant: git status 命令显示你的工作目录和暂存区的当前状态。它显示哪些更改已暂存，哪些没有，以及哪些文件没有被 Git 跟踪。

        <reasoning>
        助手没有使用待办列表，因为这是一个信息性请求，没有实际的编码任务要完成。用户只是询问一个命令的解释。
        </reasoning>
        </example>

        <example>
        User: 你能给 calculateTotal 函数添加一个注释来解释它是做什么的吗？
        Assistant: 当然，让我给 calculateTotal 函数添加一个注释来解释它的功能。

        *使用 Edit 工具给 calculateTotal 函数添加注释 *

        <reasoning>
        助手没有使用待办列表，因为这是一个单一的、直接的任务，仅限于代码中的一个位置。添加单个注释不需要跟踪多个步骤或系统组织。
        </reasoning>
        </example>

        <example>
        User: 为我运行 npm install 并告诉我发生了什么。
        Assistant: 我将为你运行 npm install 命令。

        *执行：npm install*

        命令成功完成。以下是输出：
        [npm install 命令的输出]

        所有依赖项都已根据你的 package.json 文件安装。

        <reasoning>
        助手没有使用待办列表，因为这是单个命令执行，立即产生结果。没有多个步骤要跟踪或组织。
        </reasoning>
        </example>

        ## 任务字段使用

        使用此工具时，根据任务完成情况有条件地包含 `summary` 字段：

        **在任务完成时包含 summary：**
        - 当一个或多个任务正在过渡到 completed 状态时
        - 描述实际完成的工作和取得的成果

        示例：
        "使用 JWT 令牌和安全密码哈希完成了用户认证实现"
        "完成了标题组件 CSS 的重构，解决了所有响应式设计问题"

        **省略 summary 字段时：**
        - 仅更改任务状态（例如，pending → in_progress）
        - 添加新任务或重新组织现有任务
        - 没有任务被标记为 completed

        ## 任务状态和管理

        1. **任务状态**：使用这些状态来跟踪进度：
                       - pending：任务尚未开始
                       - in_progress：当前正在处理（限制一次一个）
                       - completed：任务成功完成

           **重要**：任务描述必须有两种形式：
           - content：祈使句形式，描述需要做什么（例如"运行测试"、"构建项目"）
           - activeForm：现在进行时形式，执行期间显示（例如"正在运行测试"、"正在构建项目"）

        2. **任务管理**：
          - 实时更新任务状态
          - 完成后立即标记任务（不要批量完成）
          - 任何时刻必须恰好有一个任务处于 in_progress 状态（不能少，也不能多）
          - 在启动新任务之前完成当前任务
          - 删除不再相关的任务

        3. **任务完成要求**：
          - 只有在你完全完成任务时，才将其标记为 completed
          - 如果你遇到错误、阻塞或无法完成，请保持任务为 in_progress
          - 遇到阻塞时，创建一个新任务描述需要解决的问题
          - 在以下情况下，绝不要将任务标记为 completed：
            - 测试失败
            - 实现不完整
            - 遇到未解决的错误
            - 找不到必要的文件或依赖项

        4. **任务分解**：
          - 创建具体、可操作的条目
          - 将复杂任务分解为更小、可管理的步骤
          - 使用清晰、描述性的任务名称
          - 始终提供两种形式：
            - content："修复认证 bug"
            - activeForm："正在修复认证 bug"

        记住：目标是保持清晰、可操作的任务列表，帮助你和用户跟踪进度，而不会产生不必要的开销。
        """;

    public const string Description = "更新当前会话的待办列表。应主动且频繁地使用以跟踪进度和待处理任务。确保始终至少有一个任务处于 in_progress 状态。始终为每个任务提供 content（祈使句）和 activeForm（进行时）两种形式。";
}
