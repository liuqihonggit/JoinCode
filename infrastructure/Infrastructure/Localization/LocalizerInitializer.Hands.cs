namespace Infrastructure.Localization;

public static partial class LocalizerInitializer
{
    private static void RegisterHandsEntries(Dictionary<string, string> defaultEntries, Dictionary<string, string> zhEntries)
    {
        // === NotebookToolHandlers ===
        defaultEntries[StringKey.NotebookFilePathCannotBeEmpty] = "file_path cannot be empty";
        defaultEntries[StringKey.NotebookFileAlreadyExists] = "File already exists: {0}";
        defaultEntries[StringKey.NotebookSaveFailed] = "Failed to save notebook";
        defaultEntries[StringKey.NotebookCreatedSuccess] = "Notebook created successfully";
        defaultEntries[StringKey.NotebookPathLabel] = "Path: {0}";
        defaultEntries[StringKey.NotebookFormatVersion] = "Format version: {0}.{1}";
        defaultEntries[StringKey.NotebookKernelLabel] = "Kernel: {0}";
        defaultEntries[StringKey.NotebookFileNotExist] = "File does not exist: {0}";
        defaultEntries[StringKey.NotebookParseFailed] = "Failed to parse notebook file";
        defaultEntries[StringKey.NotebookInfoHeader] = "Notebook Info";
        defaultEntries[StringKey.NotebookTotalCells] = "Total cells: {0}";
        defaultEntries[StringKey.NotebookCodeCells] = "  - Code cells: {0}";
        defaultEntries[StringKey.NotebookMarkdownCells] = "  - Markdown cells: {0}";
        defaultEntries[StringKey.NotebookCellListHeader] = "Cell list:";
        defaultEntries[StringKey.NotebookCellContentHeader] = "Cell content:";
        defaultEntries[StringKey.NotebookCellSeparator] = "--- Cell [{0}] ({1}) ---";
        defaultEntries[StringKey.NotebookInvalidCellType] = "Invalid cell type: {0}. Valid values: code, markdown, raw";
        defaultEntries[StringKey.NotebookAddCellFailed] = "Failed to add cell";
        defaultEntries[StringKey.NotebookCellAddedSuccess] = "Cell added successfully at [{0}], type: {1}";
        defaultEntries[StringKey.NotebookDeleteCellFailed] = "Failed to delete cell";
        defaultEntries[StringKey.NotebookCellDeleted] = "Cell [{0}] deleted";
        defaultEntries[StringKey.NotebookEditCellFailed] = "Failed to edit cell";
        defaultEntries[StringKey.NotebookCellUpdated] = "Cell [{0}] content updated";
        defaultEntries[StringKey.NotebookMoveCellFailed] = "Failed to move cell";
        defaultEntries[StringKey.NotebookCellMoved] = "Cell moved from [{0}] to [{1}]";
        defaultEntries[StringKey.NotebookInvalidType] = "Invalid type: {0}. Valid values: code, markdown, raw";
        defaultEntries[StringKey.NotebookChangeCellTypeFailed] = "Failed to change cell type";
        defaultEntries[StringKey.NotebookCellTypeChanged] = "Cell [{0}] type changed to {1}";
        defaultEntries[StringKey.NotebookClearOutputsFailed] = "Failed to clear outputs";
        defaultEntries[StringKey.NotebookOutputsCleared] = "All cell outputs cleared";
        defaultEntries[StringKey.NotebookInvalidCellIndex] = "Invalid cell index: {0}";
        defaultEntries[StringKey.NotebookCellHeader] = "Cell [{0}]";
        defaultEntries[StringKey.NotebookCellTypeLabel] = "Type: {0}";
        defaultEntries[StringKey.NotebookExecutionCountLabel] = "Execution count: {0}";
        defaultEntries[StringKey.NotebookContentLabel] = "Content:";
        defaultEntries[StringKey.NotebookOutputLabel] = "Output:";

        zhEntries[StringKey.NotebookFilePathCannotBeEmpty] = "file_path 不能为空";
        zhEntries[StringKey.NotebookFileAlreadyExists] = "文件已存在: {0}";
        zhEntries[StringKey.NotebookSaveFailed] = "保存Notebook失败";
        zhEntries[StringKey.NotebookCreatedSuccess] = "Notebook创建成功";
        zhEntries[StringKey.NotebookPathLabel] = "路径: {0}";
        zhEntries[StringKey.NotebookFormatVersion] = "格式版本: {0}.{1}";
        zhEntries[StringKey.NotebookKernelLabel] = "内核: {0}";
        zhEntries[StringKey.NotebookFileNotExist] = "文件不存在: {0}";
        zhEntries[StringKey.NotebookParseFailed] = "无法解析Notebook文件";
        zhEntries[StringKey.NotebookInfoHeader] = "Notebook信息";
        zhEntries[StringKey.NotebookTotalCells] = "单元格总数: {0}";
        zhEntries[StringKey.NotebookCodeCells] = "  - 代码单元格: {0}";
        zhEntries[StringKey.NotebookMarkdownCells] = "  - Markdown单元格: {0}";
        zhEntries[StringKey.NotebookCellListHeader] = "单元格列表:";
        zhEntries[StringKey.NotebookCellContentHeader] = "单元格内容:";
        zhEntries[StringKey.NotebookCellSeparator] = "--- 单元格 [{0}] ({1}) ---";
        zhEntries[StringKey.NotebookInvalidCellType] = "无效的单元格类型: {0}。有效值: code, markdown, raw";
        zhEntries[StringKey.NotebookAddCellFailed] = "添加单元格失败";
        zhEntries[StringKey.NotebookCellAddedSuccess] = "单元格添加成功，位置: [{0}]，类型: {1}";
        zhEntries[StringKey.NotebookDeleteCellFailed] = "删除单元格失败";
        zhEntries[StringKey.NotebookCellDeleted] = "单元格 [{0}] 已删除";
        zhEntries[StringKey.NotebookEditCellFailed] = "编辑单元格失败";
        zhEntries[StringKey.NotebookCellUpdated] = "单元格 [{0}] 内容已更新";
        zhEntries[StringKey.NotebookMoveCellFailed] = "移动单元格失败";
        zhEntries[StringKey.NotebookCellMoved] = "单元格已从 [{0}] 移动到 [{1}]";
        zhEntries[StringKey.NotebookInvalidType] = "无效的类型: {0}。有效值: code, markdown, raw";
        zhEntries[StringKey.NotebookChangeCellTypeFailed] = "更改单元格类型失败";
        zhEntries[StringKey.NotebookCellTypeChanged] = "单元格 [{0}] 类型已更改为 {1}";
        zhEntries[StringKey.NotebookClearOutputsFailed] = "清除输出失败";
        zhEntries[StringKey.NotebookOutputsCleared] = "所有单元格输出已清除";
        zhEntries[StringKey.NotebookInvalidCellIndex] = "无效的单元格索引: {0}";
        zhEntries[StringKey.NotebookCellHeader] = "单元格 [{0}]";
        zhEntries[StringKey.NotebookCellTypeLabel] = "类型: {0}";
        zhEntries[StringKey.NotebookExecutionCountLabel] = "执行次数: {0}";
        zhEntries[StringKey.NotebookContentLabel] = "内容:";
        zhEntries[StringKey.NotebookOutputLabel] = "输出:";

        // === NotebookService ===
        defaultEntries[StringKey.NotebookServiceInvalidCellIndex] = "Invalid cell index: {0}";
        defaultEntries[StringKey.NotebookServiceInvalidSourceIndex] = "Invalid source index: {0}";
        defaultEntries[StringKey.NotebookServiceInvalidTargetIndex] = "Invalid target index: {0}";
        defaultEntries[StringKey.NotebookServiceOnlyCodeCellCanExecute] = "Only code cells can be executed";

        zhEntries[StringKey.NotebookServiceInvalidCellIndex] = "无效的单元格索引: {0}";
        zhEntries[StringKey.NotebookServiceInvalidSourceIndex] = "无效的源索引: {0}";
        zhEntries[StringKey.NotebookServiceInvalidTargetIndex] = "无效的目标索引: {0}";
        zhEntries[StringKey.NotebookServiceOnlyCodeCellCanExecute] = "只有代码单元格可以执行";

        // === VoiceService ===
        defaultEntries[StringKey.VoiceStartRecording] = "Recording started";
        defaultEntries[StringKey.VoiceRecordingDataEmpty] = "Recording data is empty";
        defaultEntries[StringKey.VoiceRecordingComplete] = "Recording complete: duration={0}ms, text length={1}";
        defaultEntries[StringKey.VoiceUnsupportedSttBackend] = "Unsupported STT backend: {0}";
        defaultEntries[StringKey.VoiceAudioFileNotFound] = "Audio file not found";
        defaultEntries[StringKey.VoiceWhisperApiFailed] = "Whisper API call failed: {0}";
        defaultEntries[StringKey.VoiceWhisperApiCallFailed] = "Whisper API call failed: {0}";
        defaultEntries[StringKey.VoiceLocalModelPathInvalid] = "Local model path invalid or model file does not exist";
        defaultEntries[StringKey.VoiceLocalSttNotImplemented] = "Local model STT not yet implemented";
        defaultEntries[StringKey.VoiceRecordLoopError] = "Recording loop error";

        zhEntries[StringKey.VoiceStartRecording] = "开始录音";
        zhEntries[StringKey.VoiceRecordingDataEmpty] = "录音数据为空";
        zhEntries[StringKey.VoiceRecordingComplete] = "录音完成: 时长={0}ms, 文本长度={1}";
        zhEntries[StringKey.VoiceUnsupportedSttBackend] = "不支持的 STT 后端: {0}";
        zhEntries[StringKey.VoiceAudioFileNotFound] = "音频文件未找到";
        zhEntries[StringKey.VoiceWhisperApiFailed] = "Whisper API 调用失败: {0}";
        zhEntries[StringKey.VoiceWhisperApiCallFailed] = "Whisper API 调用失败: {0}";
        zhEntries[StringKey.VoiceLocalModelPathInvalid] = "本地模型路径无效或模型文件不存在";
        zhEntries[StringKey.VoiceLocalSttNotImplemented] = "本地模型 STT 尚未实现";
        zhEntries[StringKey.VoiceRecordLoopError] = "录音循环异常";

        // === SimpleJsonSchemaValidator ===
        defaultEntries[StringKey.SchemaInvalidJsonInstance] = "Invalid JSON instance: {0}";
        defaultEntries[StringKey.SchemaInvalidJsonSchema] = "Invalid JSON Schema: {0}";
        defaultEntries[StringKey.SchemaTypeMismatch] = "Expected type '{0}', actual type '{1}'";
        defaultEntries[StringKey.SchemaEnumValueNotAllowed] = "Value not in allowed enum range";
        defaultEntries[StringKey.SchemaRequiredPropertyMissing] = "Required property '{0}' is missing";
        defaultEntries[StringKey.SchemaAdditionalPropertyNotAllowed] = "Additional property '{0}' is not allowed";
        defaultEntries[StringKey.SchemaStringTooShort] = "String length {0} is less than minimum length {1}";
        defaultEntries[StringKey.SchemaStringTooLong] = "String length {0} exceeds maximum length {1}";
        defaultEntries[StringKey.SchemaNumberTooSmall] = "Value {0} is less than minimum {1}";
        defaultEntries[StringKey.SchemaNumberTooLarge] = "Value {0} exceeds maximum {1}";
        defaultEntries[StringKey.SchemaArrayTooFewItems] = "Array contains {0} elements, minimum required is {1}";
        defaultEntries[StringKey.SchemaArrayTooManyItems] = "Array contains {0} elements, maximum allowed is {1}";

        zhEntries[StringKey.SchemaInvalidJsonInstance] = "无效的JSON实例: {0}";
        zhEntries[StringKey.SchemaInvalidJsonSchema] = "无效的JSON Schema: {0}";
        zhEntries[StringKey.SchemaTypeMismatch] = "期望类型 '{0}'，实际类型 '{1}'";
        zhEntries[StringKey.SchemaEnumValueNotAllowed] = "值不在允许的枚举范围内";
        zhEntries[StringKey.SchemaRequiredPropertyMissing] = "必需属性 '{0}' 缺失";
        zhEntries[StringKey.SchemaAdditionalPropertyNotAllowed] = "不允许的额外属性 '{0}'";
        zhEntries[StringKey.SchemaStringTooShort] = "字符串长度 {0} 小于最小长度 {1}";
        zhEntries[StringKey.SchemaStringTooLong] = "字符串长度 {0} 超过最大长度 {1}";
        zhEntries[StringKey.SchemaNumberTooSmall] = "数值 {0} 小于最小值 {1}";
        zhEntries[StringKey.SchemaNumberTooLarge] = "数值 {0} 超过最大值 {1}";
        zhEntries[StringKey.SchemaArrayTooFewItems] = "数组包含 {0} 个元素，最少需要 {1} 个";
        zhEntries[StringKey.SchemaArrayTooManyItems] = "数组包含 {0} 个元素，最多允许 {1} 个";

        // === FileEditLogic ===
        defaultEntries[StringKey.FileEditFileNotExist] = "File does not exist";
        defaultEntries[StringKey.FileEditRegexInvalid] = "Invalid regex: {0}";
        defaultEntries[StringKey.FileEditPatternNotFound] = "Pattern not found";
        defaultEntries[StringKey.FileEditLineOutOfRange] = "Line {0} out of range (0-{1})";
        defaultEntries[StringKey.FileEditStartLineGreaterThanEnd] = "Start line cannot be greater than end line";
        defaultEntries[StringKey.FileEditStartLineOutOfRange] = "Start line {0} out of range (1-{1})";
        defaultEntries[StringKey.FileEditStringNotFound] = "String to replace not found";

        zhEntries[StringKey.FileEditFileNotExist] = "文件不存在";
        zhEntries[StringKey.FileEditRegexInvalid] = "正则表达式无效: {0}";
        zhEntries[StringKey.FileEditPatternNotFound] = "未找到匹配的模式";
        zhEntries[StringKey.FileEditLineOutOfRange] = "行号 {0} 超出范围 (0-{1})";
        zhEntries[StringKey.FileEditStartLineGreaterThanEnd] = "起始行号不能大于结束行号";
        zhEntries[StringKey.FileEditStartLineOutOfRange] = "起始行号 {0} 超出范围 (1-{1})";
        zhEntries[StringKey.FileEditStringNotFound] = "未找到要替换的字符串";

        // === SkillService ===
        defaultEntries[StringKey.SkillServiceStartExecution] = "Starting skill execution: {0}";
        defaultEntries[StringKey.SkillServiceExecutionComplete] = "Skill {0} execution completed, duration: {1}ms";
        defaultEntries[StringKey.SkillServiceExecutionCancelled] = "Skill {0} execution cancelled";
        defaultEntries[StringKey.SkillServiceExecutionCancelledResult] = "Execution cancelled";
        defaultEntries[StringKey.SkillServiceExecutionFailed] = "Skill {0} execution failed";
        defaultEntries[StringKey.SkillServiceReloaded] = "Skill reloaded: {0}";
        defaultEntries[StringKey.SkillServiceReloadAll] = "All skills reloaded";
        defaultEntries[StringKey.SkillServiceReloadFailed] = "Failed to reload skills";
        defaultEntries[StringKey.SkillServiceUnsupportedStepType] = "Unsupported step type: {0}";
        defaultEntries[StringKey.SkillServiceUnknownError] = "Unknown error";
        defaultEntries[StringKey.SkillServiceToolExecutionFailed] = "Tool '{0}' execution failed: {1}";
        defaultEntries[StringKey.SkillServiceMissingRequiredParam] = "Missing required parameter: {0}";

        zhEntries[StringKey.SkillServiceStartExecution] = "开始执行技能: {0}";
        zhEntries[StringKey.SkillServiceExecutionComplete] = "技能 {0} 执行完成，耗时 {1}ms";
        zhEntries[StringKey.SkillServiceExecutionCancelled] = "技能 {0} 被取消";
        zhEntries[StringKey.SkillServiceExecutionCancelledResult] = "执行被取消";
        zhEntries[StringKey.SkillServiceExecutionFailed] = "技能 {0} 执行失败";
        zhEntries[StringKey.SkillServiceReloaded] = "已重新加载技能: {0}";
        zhEntries[StringKey.SkillServiceReloadAll] = "已重新加载所有技能";
        zhEntries[StringKey.SkillServiceReloadFailed] = "重新加载技能失败";
        zhEntries[StringKey.SkillServiceUnsupportedStepType] = "不支持的步骤类型: {0}";
        zhEntries[StringKey.SkillServiceUnknownError] = "未知错误";
        zhEntries[StringKey.SkillServiceToolExecutionFailed] = "工具 '{0}' 执行失败: {1}";
        zhEntries[StringKey.SkillServiceMissingRequiredParam] = "缺少必需参数: {0}";

        // === SkillExecutor ===
        defaultEntries[StringKey.SkillExecutorStartExecution] = "Starting skill execution: {0}";
        defaultEntries[StringKey.SkillExecutorExecutionComplete] = "Skill {0} execution completed";
        defaultEntries[StringKey.SkillExecutorExecutionCancelled] = "Skill {0} execution cancelled";
        defaultEntries[StringKey.SkillExecutorExecutionCancelledResult] = "Execution cancelled";
        defaultEntries[StringKey.SkillExecutorExecutionFailed] = "Skill {0} execution failed";
        defaultEntries[StringKey.SkillExecutorUnsupportedStepType] = "Unsupported step type: {0}";
        defaultEntries[StringKey.SkillExecutorToolExecutionFailed] = "Tool '{0}' execution failed: {1}";
        defaultEntries[StringKey.SkillExecutorConditionTrue] = "Condition is true";
        defaultEntries[StringKey.SkillExecutorConditionFalse] = "Condition is false";
        defaultEntries[StringKey.SkillExecutorMissingRequiredParam] = "Missing required parameter: {0}";

        zhEntries[StringKey.SkillExecutorStartExecution] = "开始执行技能: {0}";
        zhEntries[StringKey.SkillExecutorExecutionComplete] = "技能 {0} 执行完成";
        zhEntries[StringKey.SkillExecutorExecutionCancelled] = "技能 {0} 被取消";
        zhEntries[StringKey.SkillExecutorExecutionCancelledResult] = "执行被取消";
        zhEntries[StringKey.SkillExecutorExecutionFailed] = "技能 {0} 执行失败";
        zhEntries[StringKey.SkillExecutorUnsupportedStepType] = "不支持的步骤类型: {0}";
        zhEntries[StringKey.SkillExecutorToolExecutionFailed] = "工具 '{0}' 执行失败: {1}";
        zhEntries[StringKey.SkillExecutorConditionTrue] = "条件为真";
        zhEntries[StringKey.SkillExecutorConditionFalse] = "条件为假";
        zhEntries[StringKey.SkillExecutorMissingRequiredParam] = "缺少必需参数: {0}";

        // === CodeService ===
        defaultEntries[StringKey.CodeServiceGeneratingCode] = "Generating code for prompt";
        defaultEntries[StringKey.CodeServiceCachedCodeResult] = "Returning cached code generation result";
        defaultEntries[StringKey.CodeServiceGenerateCodePrompt] = "Generate C# code for the following requirement: {0}";
        defaultEntries[StringKey.CodeServiceGenerateCodeFailed] = "Code generation failed.";
        defaultEntries[StringKey.CodeServiceGenerateCancelled] = "Code generation cancelled";
        defaultEntries[StringKey.CodeServiceGenerateError] = "Error during code generation";
        defaultEntries[StringKey.CodeServiceGenerateException] = "Code generation failed";
        defaultEntries[StringKey.CodeServiceAnalyzingCode] = "Analyzing code...";
        defaultEntries[StringKey.CodeServiceCachedAnalysisResult] = "Returning cached code analysis result";
        defaultEntries[StringKey.CodeServiceAnalyzeCodePrompt] = "Analyze the following C# code:\n\n```csharp\n{0}\n```";
        defaultEntries[StringKey.CodeServiceAnalyzeCodeFailed] = "Code analysis failed.";
        defaultEntries[StringKey.CodeServiceAnalyzeCancelled] = "Code analysis cancelled";
        defaultEntries[StringKey.CodeServiceAnalyzeError] = "Error during code analysis";
        defaultEntries[StringKey.CodeServiceAnalyzeException] = "Code analysis failed";
        defaultEntries[StringKey.CodeServiceExecutingInSandbox] = "Executing code in secure sandbox...";
        defaultEntries[StringKey.CodeServiceCodeCannotBeEmpty] = "Error: Code cannot be empty";
        defaultEntries[StringKey.CodeServiceCodeLengthExceeded] = "Error: Code length exceeds limit (max {0} characters)";
        defaultEntries[StringKey.CodeServiceCodeValidationFailed] = "Code validation failed: {0}";
        defaultEntries[StringKey.CodeServiceCodeValidationError] = "Error: Code validation failed - {0}";
        defaultEntries[StringKey.CodeServiceExecuteCancelled] = "Code execution cancelled";
        defaultEntries[StringKey.CodeServiceExecuteFailed] = "Code execution failed";
        defaultEntries[StringKey.CodeServiceExecuteException] = "Code execution failed";
        defaultEntries[StringKey.CodeServiceExecutionResult] = "Execution result:\n{0}";

        zhEntries[StringKey.CodeServiceGeneratingCode] = "正在为以下提示生成代码";
        zhEntries[StringKey.CodeServiceCachedCodeResult] = "返回缓存的代码生成结果";
        zhEntries[StringKey.CodeServiceGenerateCodePrompt] = "请为以下需求生成 C# 代码: {0}";
        zhEntries[StringKey.CodeServiceGenerateCodeFailed] = "生成代码失败。";
        zhEntries[StringKey.CodeServiceGenerateCancelled] = "代码生成已取消";
        zhEntries[StringKey.CodeServiceGenerateError] = "生成代码时出错";
        zhEntries[StringKey.CodeServiceGenerateException] = "生成代码失败";
        zhEntries[StringKey.CodeServiceAnalyzingCode] = "正在分析代码...";
        zhEntries[StringKey.CodeServiceCachedAnalysisResult] = "返回缓存的代码分析结果";
        zhEntries[StringKey.CodeServiceAnalyzeCodePrompt] = "请分析以下 C# 代码:\n\n```csharp\n{0}\n```";
        zhEntries[StringKey.CodeServiceAnalyzeCodeFailed] = "分析代码失败。";
        zhEntries[StringKey.CodeServiceAnalyzeCancelled] = "代码分析已取消";
        zhEntries[StringKey.CodeServiceAnalyzeError] = "分析代码时出错";
        zhEntries[StringKey.CodeServiceAnalyzeException] = "分析代码失败";
        zhEntries[StringKey.CodeServiceExecutingInSandbox] = "正在安全沙箱中执行代码...";
        zhEntries[StringKey.CodeServiceCodeCannotBeEmpty] = "错误: 代码不能为空";
        zhEntries[StringKey.CodeServiceCodeLengthExceeded] = "错误: 代码长度超过限制 (最大 {0} 字符)";
        zhEntries[StringKey.CodeServiceCodeValidationFailed] = "代码验证失败: {0}";
        zhEntries[StringKey.CodeServiceCodeValidationError] = "错误: 代码验证失败 - {0}";
        zhEntries[StringKey.CodeServiceExecuteCancelled] = "代码执行已取消";
        zhEntries[StringKey.CodeServiceExecuteFailed] = "代码执行失败";
        zhEntries[StringKey.CodeServiceExecuteException] = "执行代码失败";
        zhEntries[StringKey.CodeServiceExecutionResult] = "执行结果:\n{0}";

        // === SkillDiscoveryService ===
        defaultEntries[StringKey.SkillDiscoveryCreateDir] = "Created skill directory: {0}";
        defaultEntries[StringKey.SkillDiscoveryFoundCount] = "Discovered {0} skills";
        defaultEntries[StringKey.SkillDiscoveryFileNotExist] = "File does not exist: {0}";
        defaultEntries[StringKey.SkillDiscoveryCannotReadFile] = "Cannot read file: {0}";
        defaultEntries[StringKey.SkillDiscoveryJsonNull] = "JSON deserialization returned null";
        defaultEntries[StringKey.SkillDiscoveryJsonParseError] = "JSON parse error: {0}";
        defaultEntries[StringKey.SkillDiscoveryUnsupportedExtension] = "Unsupported file extension: {0}";
        defaultEntries[StringKey.SkillDiscoveryNameEmpty] = "Skill name cannot be empty";
        defaultEntries[StringKey.SkillDiscoveryDescriptionEmpty] = "Skill description is empty, adding one is recommended";
        defaultEntries[StringKey.SkillDiscoveryNoSteps] = "Skill must contain at least one step";
        defaultEntries[StringKey.SkillDiscoveryStepMissingId] = "Step missing Id";
        defaultEntries[StringKey.SkillDiscoveryStepIdDuplicate] = "Duplicate step Id: {0}";
        defaultEntries[StringKey.SkillDiscoveryStepMissingType] = "Step {0} missing Type";
        defaultEntries[StringKey.SkillDiscoveryParamMissingType] = "Parameter {0} missing type definition";

        zhEntries[StringKey.SkillDiscoveryCreateDir] = "创建技能目录: {0}";
        zhEntries[StringKey.SkillDiscoveryFoundCount] = "发现 {0} 个技能";
        zhEntries[StringKey.SkillDiscoveryFileNotExist] = "文件不存在: {0}";
        zhEntries[StringKey.SkillDiscoveryCannotReadFile] = "无法读取文件: {0}";
        zhEntries[StringKey.SkillDiscoveryJsonNull] = "JSON 反序列化返回 null";
        zhEntries[StringKey.SkillDiscoveryJsonParseError] = "JSON 解析错误: {0}";
        zhEntries[StringKey.SkillDiscoveryUnsupportedExtension] = "不支持的文件扩展名: {0}";
        zhEntries[StringKey.SkillDiscoveryNameEmpty] = "技能名称不能为空";
        zhEntries[StringKey.SkillDiscoveryDescriptionEmpty] = "技能描述为空，建议添加描述";
        zhEntries[StringKey.SkillDiscoveryNoSteps] = "技能必须至少包含一个步骤";
        zhEntries[StringKey.SkillDiscoveryStepMissingId] = "步骤缺少 Id";
        zhEntries[StringKey.SkillDiscoveryStepIdDuplicate] = "步骤 Id 重复: {0}";
        zhEntries[StringKey.SkillDiscoveryStepMissingType] = "步骤 {0} 缺少 Type";
        zhEntries[StringKey.SkillDiscoveryParamMissingType] = "参数 {0} 缺少类型定义";

        // === ShellBackgroundTaskService ===
        defaultEntries[StringKey.ShellBgCommandCannotBeEmpty] = "Command cannot be empty";
        defaultEntries[StringKey.ShellBgTaskCreated] = "Created shell background task {0}: {1}";
        defaultEntries[StringKey.ShellBgTaskCancelled] = "Cancelled shell background task {0}";
        defaultEntries[StringKey.ShellBgTaskNotExist] = "Task does not exist: {0}";
        defaultEntries[StringKey.ShellBgStdoutLabel] = "[Stdout]";
        defaultEntries[StringKey.ShellBgStderrLabel] = "[Stderr]";
        defaultEntries[StringKey.ShellBgExecutionFailed] = "Execution failed";
        defaultEntries[StringKey.ShellBgStartExecution] = "Starting shell background task {0}";
        defaultEntries[StringKey.ShellBgTaskCancelledByException] = "Shell background task {0} cancelled";
        defaultEntries[StringKey.ShellBgTaskExecutionFailed] = "Shell background task {0} execution failed";
        defaultEntries[StringKey.ShellBgCancelAgentTasks] = "Cancelled {1} background shell tasks for agent {0}";

        zhEntries[StringKey.ShellBgCommandCannotBeEmpty] = "命令不能为空";
        zhEntries[StringKey.ShellBgTaskCreated] = "创建Shell后台任务 {0}: {1}";
        zhEntries[StringKey.ShellBgTaskCancelled] = "取消Shell后台任务 {0}";
        zhEntries[StringKey.ShellBgTaskNotExist] = "任务不存在: {0}";
        zhEntries[StringKey.ShellBgStdoutLabel] = "[标准输出]";
        zhEntries[StringKey.ShellBgStderrLabel] = "[标准错误]";
        zhEntries[StringKey.ShellBgExecutionFailed] = "执行失败";
        zhEntries[StringKey.ShellBgStartExecution] = "开始执行Shell后台任务 {0}";
        zhEntries[StringKey.ShellBgTaskCancelledByException] = "Shell后台任务 {0} 被取消";
        zhEntries[StringKey.ShellBgTaskExecutionFailed] = "Shell后台任务 {0} 执行失败";
        zhEntries[StringKey.ShellBgCancelAgentTasks] = "取消 Agent {0} 的 {1} 个后台 Shell 任务";

        // === AgentToolHandlers ===
        defaultEntries[StringKey.AgentCreateFailed] = "Failed to create agent";
        defaultEntries[StringKey.AgentListFailed] = "Failed to list agents";
        defaultEntries[StringKey.AgentSendMessageFailed] = "Failed to send message to agent";
        defaultEntries[StringKey.AgentGetMessagesFailed] = "Failed to get agent messages";
        defaultEntries[StringKey.AgentCoordinatorNotInitialized] = "Agent coordinator not initialized";
        defaultEntries[StringKey.AgentRunningCount] = "Running agents: {0}";
        defaultEntries[StringKey.AgentNoRunningAgents] = "No agents currently running.";

        zhEntries[StringKey.AgentCreateFailed] = "创建代理失败";
        zhEntries[StringKey.AgentListFailed] = "列出代理失败";
        zhEntries[StringKey.AgentSendMessageFailed] = "发送消息给代理失败";
        zhEntries[StringKey.AgentGetMessagesFailed] = "获取代理消息失败";
        zhEntries[StringKey.AgentCoordinatorNotInitialized] = "代理协调器未初始化";
        zhEntries[StringKey.AgentRunningCount] = "正在运行的代理: {0}";
        zhEntries[StringKey.AgentNoRunningAgents] = "没有正在运行的代理。";

        // === WebService ===
        // 注: WebSearchNotImplemented 已不再使用 — WebSearchAsync 通过 Anthropic API 的 web_search_20250305 实现
        defaultEntries[StringKey.WebInvalidUrl] = "Invalid URL: {0}";
        defaultEntries[StringKey.WebRedirectLimitExceeded] = "Redirect limit exceeded ({0})";
        defaultEntries[StringKey.WebRedirectMissingLocation] = "Redirect missing Location header";

        zhEntries[StringKey.WebInvalidUrl] = "无效的 URL: {0}";
        zhEntries[StringKey.WebRedirectLimitExceeded] = "重定向次数超过限制 ({0})";
        zhEntries[StringKey.WebRedirectMissingLocation] = "重定向缺少 Location 头";

        // === BriefLogic ===
        defaultEntries[StringKey.BriefFilePathEmpty] = "File path is empty";
        defaultEntries[StringKey.BriefFileNotExist] = "File does not exist";
        defaultEntries[StringKey.BriefFileSizeExceeded] = "File size exceeds limit";
        defaultEntries[StringKey.BriefProactiveLabel] = "**[Proactive]** ";
        defaultEntries[StringKey.BriefAttachmentLabel] = "[Attachment] **Attachments:**";

        zhEntries[StringKey.BriefFilePathEmpty] = "文件路径为空";
        zhEntries[StringKey.BriefFileNotExist] = "文件不存在";
        zhEntries[StringKey.BriefFileSizeExceeded] = "文件大小超出限制";
        zhEntries[StringKey.BriefProactiveLabel] = "**[主动]** ";
        zhEntries[StringKey.BriefAttachmentLabel] = "[Attachment] **附件:**";

        // === SnipLogic ===
        defaultEntries[StringKey.SnipFileNotFound] = "File not found: {0}";

        zhEntries[StringKey.SnipFileNotFound] = "文件未找到: {0}";

        // === PreventSleepService ===
        defaultEntries[StringKey.PreventSleepAlreadyActive] = "Sleep prevention already active";
        defaultEntries[StringKey.PreventSleepSetStateFailed] = "SetThreadExecutionState call failed";
        defaultEntries[StringKey.PreventSleepActivated] = "Sleep prevention activated, type: {0}";
        defaultEntries[StringKey.PreventSleepRestoreFailed] = "SetThreadExecutionState restore call failed";
        defaultEntries[StringKey.PreventSleepDeactivated] = "Sleep prevention deactivated, system can sleep normally";

        zhEntries[StringKey.PreventSleepAlreadyActive] = "防休眠已处于激活状态";
        zhEntries[StringKey.PreventSleepSetStateFailed] = "SetThreadExecutionState 调用失败";
        zhEntries[StringKey.PreventSleepActivated] = "防休眠已激活，类型: {0}";
        zhEntries[StringKey.PreventSleepRestoreFailed] = "SetThreadExecutionState 恢复调用失败";
        zhEntries[StringKey.PreventSleepDeactivated] = "防休眠已关闭，系统可正常休眠";

        // === VariableResolver ===
        defaultEntries[StringKey.VariableNotExist] = "Variable '{0}' does not exist";

        zhEntries[StringKey.VariableNotExist] = "变量 '{0}' 不存在";

        // === CodeSandboxService ===
        defaultEntries[StringKey.SandboxOutputLabel] = "[Output]";
        defaultEntries[StringKey.SandboxErrorLabel] = "[Error]";
        defaultEntries[StringKey.SandboxExitCodeLabel] = "[Exit Code] {0}";

        zhEntries[StringKey.SandboxOutputLabel] = "[输出]";
        zhEntries[StringKey.SandboxErrorLabel] = "[错误]";
        zhEntries[StringKey.SandboxExitCodeLabel] = "[退出码] {0}";

        // === PluginSkillBridge ===
        defaultEntries[StringKey.PluginSkillAlreadyRegistered] = "Skills for plugin {0} already registered, skipping";
        defaultEntries[StringKey.PluginSkillPluginNotLoaded] = "Plugin {0} not loaded, cannot register skills";
        defaultEntries[StringKey.PluginSkillRegisterFailed] = "Failed to register plugin skill: {0}/{1}";
        defaultEntries[StringKey.PluginSkillUnregisterFailed] = "Failed to unregister plugin skill: {0}/{1}";
        defaultEntries[StringKey.PluginSkillDisposeError] = "Error disposing plugin {0} skills";
        defaultEntries[StringKey.PluginSkillNoRegisteredSkills] = "Plugin {0} has no registered skills";
        defaultEntries[StringKey.PluginSkillActionParam] = "Action to execute";
        defaultEntries[StringKey.PluginSkillInputParam] = "Action input";

        zhEntries[StringKey.PluginSkillAlreadyRegistered] = "插件 {0} 的技能已注册，跳过";
        zhEntries[StringKey.PluginSkillPluginNotLoaded] = "插件 {0} 未加载，无法注册技能";
        zhEntries[StringKey.PluginSkillRegisterFailed] = "注册插件技能失败: {0}/{1}";
        zhEntries[StringKey.PluginSkillUnregisterFailed] = "注销插件技能失败: {0}/{1}";
        zhEntries[StringKey.PluginSkillDisposeError] = "释放插件 {0} 技能时出错";
        zhEntries[StringKey.PluginSkillNoRegisteredSkills] = "插件 {0} 没有注册的技能";
        zhEntries[StringKey.PluginSkillActionParam] = "要执行的操作";
        zhEntries[StringKey.PluginSkillInputParam] = "操作输入";

        // === McpSkillAdapter ===
        defaultEntries[StringKey.McpAdapterCallToolDescription] = "Call MCP tool: {0}";
        defaultEntries[StringKey.McpAdapterFormatResultDescription] = "Format tool call result";
        defaultEntries[StringKey.McpAdapterFormatResultPrompt] = "Format and display MCP tool call result";
        defaultEntries[StringKey.McpAdapterAdaptFailed] = "Failed to adapt tool {0}";
        defaultEntries[StringKey.McpAdapterToolExecutionFailed] = "MCP tool '{0}' execution failed: {1}";
        defaultEntries[StringKey.McpAdapterExecuteFailed] = "Failed to execute MCP tool {0}";
        defaultEntries[StringKey.McpAdapterBuildToolPrompt] = "Call MCP tool {0}, parameters: {1}";

        zhEntries[StringKey.McpAdapterCallToolDescription] = "调用 MCP 工具: {0}";
        zhEntries[StringKey.McpAdapterFormatResultDescription] = "格式化工具调用结果";
        zhEntries[StringKey.McpAdapterFormatResultPrompt] = "格式化并展示 MCP 工具调用的结果";
        zhEntries[StringKey.McpAdapterAdaptFailed] = "适配工具 {0} 失败";
        zhEntries[StringKey.McpAdapterToolExecutionFailed] = "MCP 工具 '{0}' 执行失败: {1}";
        zhEntries[StringKey.McpAdapterExecuteFailed] = "执行 MCP 工具 {0} 失败";
        zhEntries[StringKey.McpAdapterBuildToolPrompt] = "调用 MCP 工具 {0}，参数: {1}";

        // === McpSkillProvider ===
        defaultEntries[StringKey.McpProviderSkillNotExist] = "MCP skill does not exist: {0}";
        defaultEntries[StringKey.McpProviderAdapterNotFound] = "MCP adapter not found: {0}";
        defaultEntries[StringKey.McpProviderRefreshFailed] = "Failed to refresh MCP server {0}";
        defaultEntries[StringKey.McpProviderDisposeFailed] = "Failed to dispose MCP client";

        zhEntries[StringKey.McpProviderSkillNotExist] = "MCP 技能不存在: {0}";
        zhEntries[StringKey.McpProviderAdapterNotFound] = "找不到 MCP 适配器: {0}";
        zhEntries[StringKey.McpProviderRefreshFailed] = "刷新 MCP 服务器 {0} 失败";
        zhEntries[StringKey.McpProviderDisposeFailed] = "释放 MCP 客户端失败";

        // === API Exceptions ===
        defaultEntries[StringKey.ApiAuthFailed] = "API authentication failed: {0}";
        defaultEntries[StringKey.ApiRateLimited] = "API rate limited: {0}. Please retry later";
        defaultEntries[StringKey.ApiRateLimitedRetryAfter] = "API rate limited: {0}. Please retry after {1}s";
        defaultEntries[StringKey.ApiRateLimitedRetryLater] = "API rate limited: {0}. Please retry later";
        defaultEntries[StringKey.ApiServerError] = "API server error (HTTP {0}): {1}";
        defaultEntries[StringKey.ApiValidationFailed] = "API request validation failed: {0}";

        zhEntries[StringKey.ApiAuthFailed] = "API 认证失败: {0}";
        zhEntries[StringKey.ApiRateLimited] = "API 请求被限流: {0}. 请稍后重试";
        zhEntries[StringKey.ApiRateLimitedRetryAfter] = "API 请求被限流: {0}. 请在 {1}s 后重试";
        zhEntries[StringKey.ApiRateLimitedRetryLater] = "API 请求被限流: {0}. 请稍后重试";
        zhEntries[StringKey.ApiServerError] = "API 服务器错误 (HTTP {0}): {1}";
        zhEntries[StringKey.ApiValidationFailed] = "API 请求验证失败: {0}";

        // === RetryPolicy ===
        defaultEntries[StringKey.RetryExhausted] = "Operation failed after {0} retries";

        zhEntries[StringKey.RetryExhausted] = "操作在 {0} 次重试后仍然失败";

        // === ApiClient ===
        defaultEntries[StringKey.ApiClientRequestFailed] = "Request failed - path: {0}";
        defaultEntries[StringKey.ApiClientRequestRetry] = "Request retry - path: {0}, attempt: {1}, delay: {2}ms";
        defaultEntries[StringKey.ApiClientJsonDeserializationFailed] = "JSON deserialization failed - endpoint: {0}";
        defaultEntries[StringKey.ApiClientRequestFailedGeneric] = "Request failed (HTTP {0})";
        defaultEntries[StringKey.ApiClientDefaultAuthFailed] = "Authentication failed";
        defaultEntries[StringKey.ApiClientDefaultInvalidParams] = "Invalid request parameters";

        zhEntries[StringKey.ApiClientRequestFailed] = "请求失败 - 路径: {0}";
        zhEntries[StringKey.ApiClientRequestRetry] = "请求重试 - 路径: {0}, 尝试: {1}, 延迟: {2}ms";
        zhEntries[StringKey.ApiClientJsonDeserializationFailed] = "JSON 反序列化失败 - 端点: {0}";
        zhEntries[StringKey.ApiClientRequestFailedGeneric] = "请求失败 (HTTP {0})";
        zhEntries[StringKey.ApiClientDefaultAuthFailed] = "认证失败";
        zhEntries[StringKey.ApiClientDefaultInvalidParams] = "请求参数无效";

        // === UsageTracker ===
        defaultEntries[StringKey.UsageTrackerRecord] = "Token usage record - model: {0}, input: {1}, output: {2}, cache creation: {3}, cache read: {4}, total: {5}, cost: ${6:F6}";
        defaultEntries[StringKey.UsageTrackerExtractFailed] = "Failed to extract token usage from response";

        zhEntries[StringKey.UsageTrackerRecord] = "Token 使用记录 - 模型: {0}, 输入: {1}, 输出: {2}, 缓存创建: {3}, 缓存读取: {4}, 总: {5}, 成本: ${6:F6}";
        zhEntries[StringKey.UsageTrackerExtractFailed] = "从响应中提取 Token 使用失败";

        // === ApiLoggingHandler ===
        defaultEntries[StringKey.ApiLoggingRequestLogFailed] = "Failed to log request info";
        defaultEntries[StringKey.ApiLoggingResponseNull] = "Response is null";
        defaultEntries[StringKey.ApiLoggingResponseLogFailed] = "Failed to log response info";

        zhEntries[StringKey.ApiLoggingRequestLogFailed] = "记录请求信息失败";
        zhEntries[StringKey.ApiLoggingResponseNull] = "响应为空";
        zhEntries[StringKey.ApiLoggingResponseLogFailed] = "记录响应信息失败";

        // === MemoryCacheService ===
        defaultEntries[StringKey.CacheHit] = "Cache hit, key: {0}";
        defaultEntries[StringKey.CacheMiss] = "Cache miss, key: {0}";
        defaultEntries[StringKey.CacheSet] = "Cache set, key: {0}, expiration: {1}";
        defaultEntries[StringKey.CacheDefault30Min] = "default 30 minutes";
        defaultEntries[StringKey.CacheRemoved] = "Cache removed, key: {0}";
        defaultEntries[StringKey.CacheCleared] = "Cache cleared";

        zhEntries[StringKey.CacheHit] = "缓存命中，键: {0}";
        zhEntries[StringKey.CacheMiss] = "缓存未命中，键: {0}";
        zhEntries[StringKey.CacheSet] = "缓存已设置，键: {0}, 过期时间: {1}";
        zhEntries[StringKey.CacheDefault30Min] = "默认30分钟";
        zhEntries[StringKey.CacheRemoved] = "缓存已移除，键: {0}";
        zhEntries[StringKey.CacheCleared] = "缓存已清空";

        // === VcrService ===
        defaultEntries[StringKey.VcrNotRecordMode] = "Current mode is not record mode, skipping recording";
        defaultEntries[StringKey.VcrNotPlaybackMode] = "Current mode is not playback mode, returning null";
        defaultEntries[StringKey.VcrNoMatchingInteraction] = "No matching recorded interaction found: {0} {1}";
        defaultEntries[StringKey.VcrNoMatchingInteractionStrict] = "No matching recorded interaction found: {0} {1}";

        zhEntries[StringKey.VcrNotRecordMode] = "当前模式非录制模式，跳过录制";
        zhEntries[StringKey.VcrNotPlaybackMode] = "当前模式非回放模式，返回 null";
        zhEntries[StringKey.VcrNoMatchingInteraction] = "未找到匹配的录制交互: {0} {1}";
        zhEntries[StringKey.VcrNoMatchingInteractionStrict] = "未找到匹配的录制交互: {0} {1}";

        // === SkillSearchService ===
        defaultEntries[StringKey.SkillSearchIndexRebuilt] = "Skill search index rebuilt: {0} skills";

        zhEntries[StringKey.SkillSearchIndexRebuilt] = "技能搜索索引已重建: {0} 个技能";
    }
}
