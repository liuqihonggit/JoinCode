namespace Core.Skills.BuiltIn;

public sealed class KeybindingsSkill
{
    public static SkillDefinition CreateDefinition()
    {
        return new SkillDefinition
        {
            Name = "keybindings",
            Description = "管理键盘快捷键 — 重绑定、解绑、添加组合键到 keybindings.json",
            Version = "1.0",
            Parameters = new Dictionary<string, SkillParameter>
            {
                ["action"] = new() { Type = "string", Description = "操作类型", Required = true, Validation = new ParameterValidation { EnumValues = new List<string> { "rebind", "unbind", "add", "list", "reset" }.AsReadOnly() } },
                ["key"] = new() { Type = "string", Description = "键组合（如 ctrl+s、alt+enter、ctrl+k ctrl+t）", Required = false },
                ["command"] = new() { Type = "string", Description = "绑定的命令（如 chat:externalEditor）", Required = false },
                ["context"] = new() { Type = "string", Description = "上下文（如 Global、Chat、Autocomplete）", Required = false, DefaultValue = "Global" }
            },
            Steps = new List<SkillStep>
            {
                new() { Id = "read_keybindings", Type = SkillStepType.Tool, Tool = FileToolNameConstants.FileRead, Description = "读取现有键绑定文件", Prompt = "读取 ~/.jcc/keybindings.json。如果文件不存在，说明需要创建新文件。", Next = "analyze" },
                new() { Id = "analyze", Type = SkillStepType.Prompt, Description = "分析键绑定变更", Prompt = "分析键绑定变更请求：\n\n操作：{{action}}\n键：{{key}}\n命令：{{command}}\n上下文：{{context}}\n\n请确定：\n1. 现有键绑定结构\n2. 是否与现有绑定冲突\n3. 键组合语法是否正确（修饰键用 + 连接，组合键用空格分隔）\n4. 命令是否存在于可用命令列表中", Next = "apply" },
                new() { Id = "apply", Type = SkillStepType.Tool, Tool = FileToolNameConstants.FileEdit, Description = "应用键绑定变更", Prompt = "根据操作类型修改 keybindings.json：\n\n- rebind: 解绑旧键 + 绑定新键\n- unbind: 将键设为 null\n- add: 添加新绑定\n- list: 仅展示，不修改\n- reset: 删除用户自定义，恢复默认\n\n确保 JSON 格式正确，保留现有绑定。", Next = "verify" },
                new() { Id = "verify", Type = SkillStepType.Prompt, Description = "验证键绑定", Prompt = "验证键绑定变更：\n\n1. JSON 语法正确\n2. 键组合 {{key}} 已按操作 {{action}} 处理\n3. 无冲突的绑定\n4. 上下文 {{context}} 有效\n\n如有问题请说明。" }
            },
            RequiresConfirmation = false,
            TimeoutSeconds = 30,
            Tags = new List<string> { "keybindings", "keyboard", "shortcut" }.AsReadOnly(),
            Permissions = new List<string> { "file.read", "file.edit" }.AsReadOnly()
        };
    }
}
