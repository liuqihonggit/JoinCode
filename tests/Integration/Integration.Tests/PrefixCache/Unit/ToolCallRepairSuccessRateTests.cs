namespace Integration.Tests.PrefixCache.Unit;

/// <summary>
/// 工具调用宽容处理成功率验证测试
/// 模拟真实 LLM（DeepSeek/OpenAI/Anthropic）返回的脏工具调用，验证三层修复链路的成功率
/// 三层修复：RepairToolName（工具名归一化）→ RepairJson（JSON 语法修复）→ RepairArguments（参数名+类型修复）
/// 基准对比：opus4.8 无宽容处理，遇到脏调用直接失败（成功率 0%）
/// </summary>
public sealed class ToolCallRepairSuccessRateTests
{
    /// <summary>
    /// 脏工具调用样本 — 模拟 LLM 真实输出
    /// </summary>
    private sealed record DirtyToolCallSample
    {
        public required string Category { get; init; }
        public required string Description { get; init; }
        public required string RawToolName { get; init; }
        public required string RawArguments { get; init; }
        public required string ExpectedToolName { get; init; }
        public required string ExpectedArgumentsKey { get; init; }
        public required ToolSchema Schema { get; init; }
        public required Func<Dictionary<string, JsonElement>, bool> ValidateResult { get; init; }
    }

    /// <summary>
    /// Read 工具的标准 Schema — 用于参数名+类型修复
    /// </summary>
    private static readonly ToolSchema ReadToolSchema = new()
    {
        Properties = new Dictionary<string, ToolSchemaProperty>
        {
            ["filePath"] = new() { Type = "string" },
            ["offset"] = new() { Type = "integer" },
            ["limit"] = new() { Type = "integer" }
        }
    };

    /// <summary>
    /// Bash 工具的标准 Schema
    /// </summary>
    private static readonly ToolSchema BashToolSchema = new()
    {
        Properties = new Dictionary<string, ToolSchemaProperty>
        {
            ["command"] = new() { Type = "string" },
            ["workingDirectory"] = new() { Type = "string" },
            ["timeout"] = new() { Type = "integer" }
        }
    };

    /// <summary>
    /// Search 工具的标准 Schema
    /// </summary>
    private static readonly ToolSchema SearchToolSchema = new()
    {
        Properties = new Dictionary<string, ToolSchemaProperty>
        {
            ["query"] = new() { Type = "string" },
            ["pattern"] = new() { Type = "string" },
            ["recursive"] = new() { Type = "boolean" }
        }
    };

    #region 类别1: 工具名大小写问题（10个样本）

    public static IEnumerable<object[]> ToolNameCaseSamples => new[]
    {
        new object[] { "read", "Read", """{"filePath":"/src/Program.cs"}""" },
        new object[] { "READ", "Read", """{"filePath":"/src/Program.cs"}""" },
        new object[] { "rEaD", "Read", """{"filePath":"/src/Program.cs"}""" },
        new object[] { "write", "Write", """{"filePath":"/src/Program.cs"}""" },
        new object[] { "WRITE", "Write", """{"filePath":"/src/Program.cs"}""" },
        new object[] { "glob", "Glob", """{"pattern":"*.cs"}""" },
        new object[] { "GLOB", "Glob", """{"pattern":"*.cs"}""" },
        new object[] { "bash", "Bash", """{"command":"ls"}""" },
        new object[] { "BASH", "Bash", """{"command":"ls"}""" },
        new object[] { "webfetch", "WebFetch", """{"url":"https://example.com"}""" }
    };

    [Theory]
    [MemberData(nameof(ToolNameCaseSamples))]
    public void SuccessRate_ToolNameCase_NormalizesCorrectly(string rawName, string expectedName, string args)
    {
        var repairedName = ToolCallRepairService.RepairToolName(rawName);
        repairedName.Should().Be(expectedName, $"工具名 {rawName} 应归一化为 {expectedName}");

        var jsonRepair = ToolCallRepairService.RepairJson(args);
        jsonRepair.Success.Should().BeTrue("JSON 应该是有效的");
    }

    #endregion

    #region 类别2: JSON 语法错误（12个样本）

    public static IEnumerable<object[]> JsonSyntaxSamples => new[]
    {
        // 尾随逗号
        new object[] { """{"filePath":"/src/Program.cs",}""", "filePath", "/src/Program.cs" },
        new object[] { """{"paths":["/a","/b",]}""", "paths", "" },
        new object[] { """{"command":"ls","workingDirectory":"/tmp",}""", "command", "ls" },
        // 未引用键
        new object[] { """{filePath: "/src/Program.cs"}""", "filePath", "/src/Program.cs" },
        new object[] { """{command: "ls", workingDirectory: "/tmp"}""", "command", "ls" },
        // 单引号
        new object[] { """{'filePath': '/src/Program.cs'}""", "filePath", "/src/Program.cs" },
        new object[] { """{'command': 'ls -la', 'workingDirectory': '/tmp'}""", "command", "ls -la" },
        // 混合问题
        new object[] { """{'command': "ls", 'workingDirectory': "/tmp",}""", "command", "ls" },
        new object[] { """{command: 'ls', workingDirectory: '/tmp',}""", "command", "ls" },
        new object[] { """{'filePath': '/src/Program.cs',}""", "filePath", "/src/Program.cs" },
        // 空参数
        new object[] { "", "", "" },
        new object[] { "   ", "", "" }
    };

    [Theory]
    [MemberData(nameof(JsonSyntaxSamples))]
    public void SuccessRate_JsonSyntax_RepairAndParse(string rawJson, string expectedKey, string expectedValue)
    {
        var jsonRepair = ToolCallRepairService.RepairJson(rawJson);
        jsonRepair.Success.Should().BeTrue($"JSON 应修复成功: {rawJson}");

        if (string.IsNullOrEmpty(expectedKey))
        {
            jsonRepair.RepairedJson.Should().Be("{}");
            return;
        }

        var parsed = JsonArgumentParser.Parse(jsonRepair.RepairedJson);
        parsed.Should().ContainKey(expectedKey);
        if (!string.IsNullOrEmpty(expectedValue))
        {
            parsed[expectedKey].GetString().Should().Be(expectedValue);
        }
    }

    #endregion

    #region 类别3: 参数名错误（12个样本）

    public static IEnumerable<object[]> ParameterNameSamples => new[]
    {
        // file_path → filePath
        new object[] { "Read", ReadToolSchema, """{"file_path":"/src/Program.cs"}""", "filePath", "/src/Program.cs" },
        new object[] { "Read", ReadToolSchema, """{"path":"/src/Program.cs"}""", "filePath", "/src/Program.cs" },
        new object[] { "Read", ReadToolSchema, """{"file":"/src/Program.cs"}""", "filePath", "/src/Program.cs" },
        // cmd → command
        new object[] { "Bash", BashToolSchema, """{"cmd":"ls -la"}""", "command", "ls -la" },
        new object[] { "Bash", BashToolSchema, """{"script":"ls -la"}""", "command", "ls -la" },
        new object[] { "Bash", BashToolSchema, """{"command_text":"ls -la"}""", "command", "ls -la" },
        // search_query → query
        new object[] { "Search", SearchToolSchema, """{"search_query":"test"}""", "query", "test" },
        new object[] { "Search", SearchToolSchema, """{"search_term":"test"}""", "query", "test" },
        // search_pattern → pattern
        new object[] { "Search", SearchToolSchema, """{"search_pattern":"*.cs"}""", "pattern", "*.cs" },
        new object[] { "Search", SearchToolSchema, """{"regex_pattern":"*.cs"}""", "pattern", "*.cs" },
        // line_number → lineNumber (snake_case → camelCase)
        new object[] { "Read", ReadToolSchema, """{"filePath":"/src/Program.cs","line_number":10}""", "filePath", "/src/Program.cs" },
        // url 变体
        new object[] { "WebFetch", new ToolSchema { Properties = new Dictionary<string, ToolSchemaProperty> { ["url"] = new() { Type = "string" } } }, """{"url_link":"https://example.com"}""", "url", "https://example.com" }
    };

    [Theory]
    [MemberData(nameof(ParameterNameSamples))]
    public void SuccessRate_ParameterName_RenamesToCorrectName(
        string toolName, ToolSchema schema, string rawJson, string expectedKey, string expectedValue)
    {
        var jsonRepair = ToolCallRepairService.RepairJson(rawJson);
        jsonRepair.Success.Should().BeTrue();

        var parsed = JsonArgumentParser.Parse(jsonRepair.RepairedJson);
        var repaired = ToolCallRepairService.RepairArguments(toolName, parsed, schema);

        repaired.RepairedArguments.Should().ContainKey(expectedKey);
        repaired.RepairedArguments[expectedKey].GetString().Should().Be(expectedValue);
    }

    #endregion

    #region 类别4: 参数类型错误（10个样本）

    public static IEnumerable<object[]> ParameterTypeSamples => new[]
    {
        // 字符串数字 → integer
        new object[] { "Read", ReadToolSchema, """{"filePath":"/src/Program.cs","offset":"10"}""", "offset", "integer" },
        new object[] { "Read", ReadToolSchema, """{"filePath":"/src/Program.cs","limit":"50"}""", "limit", "integer" },
        new object[] { "Bash", BashToolSchema, """{"command":"ls","timeout":"30"}""", "timeout", "integer" },
        // 字符串布尔 → boolean
        new object[] { "Search", SearchToolSchema, """{"query":"test","recursive":"true"}""", "recursive", "boolean" },
        new object[] { "Search", SearchToolSchema, """{"query":"test","recursive":"false"}""", "recursive", "boolean" },
        // 数字 → string
        new object[] { "Read", ReadToolSchema, """{"filePath":123}""", "filePath", "string" },
        new object[] { "Bash", BashToolSchema, """{"command":456}""", "command", "string" },
        // 布尔 → string
        new object[] { "Bash", BashToolSchema, """{"command":true}""", "command", "string" },
        // 单元素数组 → string
        new object[] { "Bash", BashToolSchema, """{"command":["ls"]}""", "command", "string" },
        new object[] { "Read", ReadToolSchema, """{"filePath":["/src/Program.cs"]}""", "filePath", "string" }
    };

    [Theory]
    [MemberData(nameof(ParameterTypeSamples))]
    public void SuccessRate_ParameterType_ConvertsToCorrectType(
        string toolName, ToolSchema schema, string rawJson, string expectedKey, string expectedType)
    {
        var jsonRepair = ToolCallRepairService.RepairJson(rawJson);
        jsonRepair.Success.Should().BeTrue();

        var parsed = JsonArgumentParser.Parse(jsonRepair.RepairedJson);
        var repaired = ToolCallRepairService.RepairArguments(toolName, parsed, schema);

        repaired.RepairedArguments.Should().ContainKey(expectedKey);
        var value = repaired.RepairedArguments[expectedKey];
        var actualType = expectedType switch
        {
            "integer" => value.ValueKind == JsonValueKind.Number ? "integer" : value.ValueKind.ToString(),
            "boolean" => value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False ? "boolean" : value.ValueKind.ToString(),
            "string" => value.ValueKind == JsonValueKind.String ? "string" : value.ValueKind.ToString(),
            _ => value.ValueKind.ToString()
        };
        actualType.Should().Be(expectedType, $"参数 {expectedKey} 应转换为 {expectedType} 类型");
    }

    #endregion

    #region 类别5: 混合问题（12个样本）— 最接近真实场景

    public static IEnumerable<object[]> MixedProblemSamples => new[]
    {
        // 小写工具名 + 单引号 + 尾随逗号 + snake_case 参数名
        new object[] { "read", "Read", ReadToolSchema, """{'file_path': '/src/Program.cs',}""", "filePath", "/src/Program.cs" },
        // 大写工具名 + 未引用键 + 字符串数字
        new object[] { "READ", "Read", ReadToolSchema, """{file_path: '/src/Program.cs', offset: '10'}""", "filePath", "/src/Program.cs" },
        // 小写工具名 + 混合引号 + 别名参数
        new object[] { "bash", "Bash", BashToolSchema, """{'cmd': "ls", 'workingDirectory': '/tmp',}""", "command", "ls" },
        // 大写工具名 + 单引号 + 字符串布尔
        new object[] { "SEARCH", "search", SearchToolSchema, """{'search_query': 'test', 'recursive': 'true',}""", "query", "test" },
        // 混合大小写 + 尾随逗号 + 类型错误
        new object[] { "rEaD", "Read", ReadToolSchema, """{'file_path': '/src/Program.cs', 'limit': '50',}""", "filePath", "/src/Program.cs" },
        // 小写 + 未引用 + 数字当字符串
        new object[] { "write", "Write", ReadToolSchema, """{file_path: 123}""", "filePath", "123" },
        // 大写 + 单引号 + 数组当字符串
        new object[] { "BASH", "Bash", BashToolSchema, """{'cmd': ['ls -la']}""", "command", "ls -la" },
        // 混合 + 多个问题
        new object[] { "glob", "Glob", SearchToolSchema, """{search_pattern: '*.cs',}""", "pattern", "*.cs" },
        // 工具名 + JSON 完全畸形
        new object[] { "read", "Read", ReadToolSchema, """{'file_path': '/src/Program.cs', 'offset': '10', 'limit': '50',}""", "filePath", "/src/Program.cs" },
        // DeepSeek 风格 — 单引号 + 未引用混合
        new object[] { "bash", "Bash", BashToolSchema, """{'command': "ls -la", workingDirectory: '/tmp'}""", "command", "ls -la" },
        // OpenAI 风格 — 大小写混乱
        new object[] { "WebFetch", "WebFetch", new ToolSchema { Properties = new Dictionary<string, ToolSchemaProperty> { ["url"] = new() { Type = "string" } } }, """{'url_link': 'https://example.com',}""", "url", "https://example.com" },
        // Anthropic 风格 — snake_case + 尾随逗号
        new object[] { "edit", "Edit", ReadToolSchema, """{'file_path': '/src/Program.cs', 'old_string': 'old', 'new_string': 'new',}""", "filePath", "/src/Program.cs" }
    };

    [Theory]
    [MemberData(nameof(MixedProblemSamples))]
    public void SuccessRate_MixedProblems_FullPipelineRepairs(
        string rawToolName, string expectedToolName, ToolSchema schema,
        string rawJson, string expectedKey, string expectedValue)
    {
        // 第一层：工具名归一化
        var repairedName = ToolCallRepairService.RepairToolName(rawToolName);
        repairedName.Should().Be(expectedToolName, $"工具名 {rawToolName} 应归一化为 {expectedToolName}");

        // 第二层：JSON 语法修复
        var jsonRepair = ToolCallRepairService.RepairJson(rawJson);
        jsonRepair.Success.Should().BeTrue($"JSON 应修复成功: {rawJson}");

        // 第三层：参数名+类型修复
        var parsed = JsonArgumentParser.Parse(jsonRepair.RepairedJson);
        var repaired = ToolCallRepairService.RepairArguments(expectedToolName, parsed, schema);

        repaired.RepairedArguments.Should().ContainKey(expectedKey);
        repaired.RepairedArguments[expectedKey].GetString().Should().Be(expectedValue);
    }

    #endregion

    #region 成功率统计报告

    [Fact]
    public void SuccessRate_Report_GeneratesStatistics()
    {
        var samples = GetAllSamples();
        var totalSamples = samples.Count;
        var successCount = 0;
        var failures = new List<string>();

        foreach (var sample in samples)
        {
            try
            {
                // 第一层：工具名归一化
                var repairedName = ToolCallRepairService.RepairToolName(sample.RawToolName);
                if (repairedName != sample.ExpectedToolName)
                {
                    failures.Add($"[{sample.Category}] {sample.Description}: 工具名修复失败 {sample.RawToolName} → {repairedName} (期望 {sample.ExpectedToolName})");
                    continue;
                }

                // 第二层：JSON 语法修复
                var jsonRepair = ToolCallRepairService.RepairJson(sample.RawArguments);
                if (!jsonRepair.Success)
                {
                    failures.Add($"[{sample.Category}] {sample.Description}: JSON 修复失败");
                    continue;
                }

                // 第三层：参数名+类型修复
                var parsed = JsonArgumentParser.Parse(jsonRepair.RepairedJson);
                var repaired = ToolCallRepairService.RepairArguments(sample.ExpectedToolName, parsed, sample.Schema);

                if (sample.ValidateResult(repaired.RepairedArguments))
                {
                    successCount++;
                }
                else
                {
                    failures.Add($"[{sample.Category}] {sample.Description}: 参数验证失败");
                }
            }
            catch (Exception ex)
            {
                failures.Add($"[{sample.Category}] {sample.Description}: 异常 {ex.Message}");
            }
        }

        var successRate = (double)successCount / totalSamples * 100;
        var report = $@"
=== 工具调用宽容处理成功率报告 ===
样本总数: {totalSamples}
成功数: {successCount}
失败数: {totalSamples - successCount}
成功率: {successRate:F2}%
基准对比: opus4.8 无宽容处理 = 0% 成功率
提升幅度: +{successRate:F2}%
==============================
";

        // 输出报告到测试输出
        failures.ForEach(f => Console.WriteLine(f));
        Console.WriteLine(report);

        // 成功率应达到 95% 以上（允许少量边界场景失败）
        successRate.Should().BeGreaterThanOrEqualTo(95.0,
            $"宽容处理成功率应达到 95% 以上。失败列表:\n{string.Join("\n", failures)}");
    }

    private static List<DirtyToolCallSample> GetAllSamples() =>
    [
        // 类别1: 工具名大小写
        new DirtyToolCallSample
        {
            Category = "工具名大小写",
            Description = "read → Read",
            RawToolName = "read",
            RawArguments = """{"filePath":"/src/Program.cs"}""",
            ExpectedToolName = "Read",
            ExpectedArgumentsKey = "filePath",
            Schema = ReadToolSchema,
            ValidateResult = args => args.TryGetValue("filePath", out var v) && v.GetString() == "/src/Program.cs"
        },
        new DirtyToolCallSample
        {
            Category = "工具名大小写",
            Description = "READ → Read",
            RawToolName = "READ",
            RawArguments = """{"filePath":"/src/Program.cs"}""",
            ExpectedToolName = "Read",
            ExpectedArgumentsKey = "filePath",
            Schema = ReadToolSchema,
            ValidateResult = args => args.TryGetValue("filePath", out var v) && v.GetString() == "/src/Program.cs"
        },
        new DirtyToolCallSample
        {
            Category = "工具名大小写",
            Description = "rEaD → Read",
            RawToolName = "rEaD",
            RawArguments = """{"filePath":"/src/Program.cs"}""",
            ExpectedToolName = "Read",
            ExpectedArgumentsKey = "filePath",
            Schema = ReadToolSchema,
            ValidateResult = args => args.TryGetValue("filePath", out var v) && v.GetString() == "/src/Program.cs"
        },
        new DirtyToolCallSample
        {
            Category = "工具名大小写",
            Description = "bash → Bash",
            RawToolName = "bash",
            RawArguments = """{"command":"ls"}""",
            ExpectedToolName = "Bash",
            ExpectedArgumentsKey = "command",
            Schema = BashToolSchema,
            ValidateResult = args => args.TryGetValue("command", out var v) && v.GetString() == "ls"
        },
        new DirtyToolCallSample
        {
            Category = "工具名大小写",
            Description = "BASH → Bash",
            RawToolName = "BASH",
            RawArguments = """{"command":"ls"}""",
            ExpectedToolName = "Bash",
            ExpectedArgumentsKey = "command",
            Schema = BashToolSchema,
            ValidateResult = args => args.TryGetValue("command", out var v) && v.GetString() == "ls"
        },

        // 类别2: JSON 语法错误
        new DirtyToolCallSample
        {
            Category = "JSON语法",
            Description = "尾随逗号",
            RawToolName = "Read",
            RawArguments = """{"filePath":"/src/Program.cs",}""",
            ExpectedToolName = "Read",
            ExpectedArgumentsKey = "filePath",
            Schema = ReadToolSchema,
            ValidateResult = args => args.TryGetValue("filePath", out var v) && v.GetString() == "/src/Program.cs"
        },
        new DirtyToolCallSample
        {
            Category = "JSON语法",
            Description = "未引用键",
            RawToolName = "Read",
            RawArguments = """{filePath: "/src/Program.cs"}""",
            ExpectedToolName = "Read",
            ExpectedArgumentsKey = "filePath",
            Schema = ReadToolSchema,
            ValidateResult = args => args.TryGetValue("filePath", out var v) && v.GetString() == "/src/Program.cs"
        },
        new DirtyToolCallSample
        {
            Category = "JSON语法",
            Description = "单引号",
            RawToolName = "Read",
            RawArguments = """{'filePath': '/src/Program.cs'}""",
            ExpectedToolName = "Read",
            ExpectedArgumentsKey = "filePath",
            Schema = ReadToolSchema,
            ValidateResult = args => args.TryGetValue("filePath", out var v) && v.GetString() == "/src/Program.cs"
        },
        new DirtyToolCallSample
        {
            Category = "JSON语法",
            Description = "混合引号+尾随逗号",
            RawToolName = "Bash",
            RawArguments = """{'command': "ls", 'workingDirectory': "/tmp",}""",
            ExpectedToolName = "Bash",
            ExpectedArgumentsKey = "command",
            Schema = BashToolSchema,
            ValidateResult = args => args.TryGetValue("command", out var v) && v.GetString() == "ls"
        },

        // 类别3: 参数名错误
        new DirtyToolCallSample
        {
            Category = "参数名",
            Description = "file_path → filePath",
            RawToolName = "Read",
            RawArguments = """{"file_path":"/src/Program.cs"}""",
            ExpectedToolName = "Read",
            ExpectedArgumentsKey = "filePath",
            Schema = ReadToolSchema,
            ValidateResult = args => args.TryGetValue("filePath", out var v) && v.GetString() == "/src/Program.cs"
        },
        new DirtyToolCallSample
        {
            Category = "参数名",
            Description = "path → filePath",
            RawToolName = "Read",
            RawArguments = """{"path":"/src/Program.cs"}""",
            ExpectedToolName = "Read",
            ExpectedArgumentsKey = "filePath",
            Schema = ReadToolSchema,
            ValidateResult = args => args.TryGetValue("filePath", out var v) && v.GetString() == "/src/Program.cs"
        },
        new DirtyToolCallSample
        {
            Category = "参数名",
            Description = "cmd → command",
            RawToolName = "Bash",
            RawArguments = """{"cmd":"ls -la"}""",
            ExpectedToolName = "Bash",
            ExpectedArgumentsKey = "command",
            Schema = BashToolSchema,
            ValidateResult = args => args.TryGetValue("command", out var v) && v.GetString() == "ls -la"
        },
        new DirtyToolCallSample
        {
            Category = "参数名",
            Description = "search_query → query",
            RawToolName = "Search",
            RawArguments = """{"search_query":"test"}""",
            ExpectedToolName = "search",
            ExpectedArgumentsKey = "query",
            Schema = SearchToolSchema,
            ValidateResult = args => args.TryGetValue("query", out var v) && v.GetString() == "test"
        },

        // 类别4: 参数类型错误
        new DirtyToolCallSample
        {
            Category = "参数类型",
            Description = "字符串数字 → integer",
            RawToolName = "Read",
            RawArguments = """{"filePath":"/src/Program.cs","offset":"10"}""",
            ExpectedToolName = "Read",
            ExpectedArgumentsKey = "offset",
            Schema = ReadToolSchema,
            ValidateResult = args => args.TryGetValue("offset", out var v) && v.ValueKind == JsonValueKind.Number
        },
        new DirtyToolCallSample
        {
            Category = "参数类型",
            Description = "字符串布尔 → boolean",
            RawToolName = "Search",
            RawArguments = """{"query":"test","recursive":"true"}""",
            ExpectedToolName = "search",
            ExpectedArgumentsKey = "recursive",
            Schema = SearchToolSchema,
            ValidateResult = args => args.TryGetValue("recursive", out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False)
        },
        new DirtyToolCallSample
        {
            Category = "参数类型",
            Description = "数字 → string",
            RawToolName = "Read",
            RawArguments = """{"filePath":123}""",
            ExpectedToolName = "Read",
            ExpectedArgumentsKey = "filePath",
            Schema = ReadToolSchema,
            ValidateResult = args => args.TryGetValue("filePath", out var v) && v.ValueKind == JsonValueKind.String && v.GetString() == "123"
        },

        // 类别5: 混合问题
        new DirtyToolCallSample
        {
            Category = "混合问题",
            Description = "小写+单引号+尾随逗号+snake_case",
            RawToolName = "read",
            RawArguments = """{'file_path': '/src/Program.cs',}""",
            ExpectedToolName = "Read",
            ExpectedArgumentsKey = "filePath",
            Schema = ReadToolSchema,
            ValidateResult = args => args.TryGetValue("filePath", out var v) && v.GetString() == "/src/Program.cs"
        },
        new DirtyToolCallSample
        {
            Category = "混合问题",
            Description = "大写+未引用+字符串数字",
            RawToolName = "READ",
            RawArguments = """{file_path: '/src/Program.cs', offset: '10'}""",
            ExpectedToolName = "Read",
            ExpectedArgumentsKey = "filePath",
            Schema = ReadToolSchema,
            ValidateResult = args => args.TryGetValue("filePath", out var v) && v.GetString() == "/src/Program.cs"
        },
        new DirtyToolCallSample
        {
            Category = "混合问题",
            Description = "小写+混合引号+别名",
            RawToolName = "bash",
            RawArguments = """{'cmd': "ls", 'workingDirectory': '/tmp',}""",
            ExpectedToolName = "Bash",
            ExpectedArgumentsKey = "command",
            Schema = BashToolSchema,
            ValidateResult = args => args.TryGetValue("command", out var v) && v.GetString() == "ls"
        },
        new DirtyToolCallSample
        {
            Category = "混合问题",
            Description = "DeepSeek风格 — 单引号+未引用混合",
            RawToolName = "bash",
            RawArguments = """{'command': "ls -la", workingDirectory: '/tmp'}""",
            ExpectedToolName = "Bash",
            ExpectedArgumentsKey = "command",
            Schema = BashToolSchema,
            ValidateResult = args => args.TryGetValue("command", out var v) && v.GetString() == "ls -la"
        }
    ];

    #endregion
}
