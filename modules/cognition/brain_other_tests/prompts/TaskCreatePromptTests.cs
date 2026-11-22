namespace Core.Tests.Prompts;

/// <summary>
/// TaskCreate 提示词必须只描述 handler 真正声明的字段（TASK103 #44）。
/// 提示词里出现 schema 不存在的字段会让模型稳定调用失败，属于"文档撒谎"。
/// </summary>
public class TaskCreatePromptTests {
    [Fact]
    public void GetPrompt_DeclaresOnlyHandlerFields() {
        var prompt = JoinCode.Abstractions.Prompts.ToolPrompts.TaskCreateToolPrompt.GetPrompt();

        prompt.Should().Contain("**title**");
        prompt.Should().NotContain("**subject**", "schema 规范名是 title");
        prompt.Should().NotContain("**activeForm**", "task_create 无该字段说明（todo_write 的 activeForm 属另一条链）");
        prompt.Should().Contain("pending");
    }
}
