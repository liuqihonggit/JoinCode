namespace Core.Skills.BuiltIn;

public sealed class UpdateConfigSkill
{
    public static SkillDefinition CreateDefinition()
    {
        return new SkillDefinition
        {
            Name = "update-config",
            Description = "更新 jcc 配置（settings.json）— 权限、环境变量、钩子、模型设置等",
            Version = "1.0",
            Parameters = new Dictionary<string, SkillParameter>
            {
                ["target_file"] = new() { Type = "string", Description = "目标配置文件路径（如 settings.json、settings.local.json）", Required = true },
                ["setting_path"] = new() { Type = "string", Description = "配置项路径（如 permissions.allow、env.DEBUG）", Required = true },
                ["value"] = new() { Type = "string", Description = "新值（JSON 格式）", Required = true },
                ["merge"] = new() { Type = "boolean", Description = "是否合并（对数组追加而非替换）", Required = false, DefaultValue = true }
            },
            Steps = new List<SkillStep>
            {
                new() { Id = "read_config", Type = SkillStepType.Tool, Tool = FileToolNameConstants.FileRead, Description = "读取现有配置文件", Prompt = "读取 {{target_file}} 的内容。如果文件不存在，说明需要创建新文件。", Next = "analyze" },
                new() { Id = "analyze", Type = SkillStepType.Prompt, Description = "分析现有配置和请求变更", Prompt = "分析现有配置文件内容，确定如何应用变更：\n\n目标文件：{{target_file}}\n配置路径：{{setting_path}}\n新值：{{value}}\n合并模式：{{merge}}\n\n请确定：\n1. 现有配置结构\n2. 需要修改的具体位置\n3. 是否需要合并数组（如权限列表）\n4. 是否会覆盖现有设置", Next = "apply_change" },
                new() { Id = "apply_change", Type = SkillStepType.Tool, Tool = FileToolNameConstants.FileEdit, Description = "应用配置变更", Prompt = "使用 Edit 工具修改 {{target_file}}：\n\n配置路径：{{setting_path}}\n新值：{{value}}\n\n注意：\n- 保持 JSON 格式正确\n- 合并数组时追加而非替换\n- 保留现有配置项", Next = "validate" },
                new() { Id = "validate", Type = SkillStepType.Prompt, Description = "验证配置变更", Prompt = "验证配置变更结果：\n\n1. JSON 语法是否正确\n2. 配置路径 {{setting_path}} 是否已更新\n3. 现有配置是否保留\n4. 变更是否符合用户预期\n\n如有问题，请说明并建议修复方案。" }
            },
            RequiresConfirmation = false,
            TimeoutSeconds = 60,
            Tags = new List<string> { "config", "settings", "configuration" }.AsReadOnly(),
            Permissions = new List<string> { "file.read", "file.edit" }.AsReadOnly()
        };
    }
}
