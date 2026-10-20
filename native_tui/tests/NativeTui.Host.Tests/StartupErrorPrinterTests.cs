namespace NativeTui.Host.Tests;

/// <summary>
/// 启动配置错误友好呈现测试（修复：配置错误直接闪退刷未处理异常堆栈）—
/// StartupErrorPrinter 把 ConfigurationException 翻译成结构化指引：
/// 标题+原因 → 检查点（两级 profile 覆盖链 + auth key 状态）→ 修复建议（改指针/补 key/环境变量）。
/// 错误诱导四要素（AGENTS.md 规矩9）：为什么失败/什么参数触发/如何修复/下一步怎么做。
/// </summary>
public sealed class StartupErrorPrinterTests
{
    /// <summary>探测数据假体 — 固定 profile 覆盖链与 key 状态（项目级 openai 覆盖用户级，key 缺失）。</summary>
    private static StartupErrorPrinter.ProbeResult FakeProbe(string? user = "zhipu", string? project = "openai", int authLength = 0) =>
        new(UserProfile: user, ProjectProfile: project, AuthVendor: "openai", AuthKeyLength: authLength, EnvVarName: "OPENAI_API_KEY", EnvVarValue: null);

    /// <summary>标题+核心原因 — 含供应商名与错误消息。</summary>
    [Fact]
    public void Format_ContainsTitleAndReason()
    {
        var ex = new ConfigurationException("Provider 'openai' 配置无效: 缺少 API Key");

        var text = StartupErrorPrinter.Format(ex, FakeProbe());

        Assert.Contains("jcc 启动失败", text);
        Assert.Contains("openai", text);
        Assert.Contains("缺少 API Key", text);
    }

    /// <summary>检查点 — 两级 profile 覆盖链可见（项目级覆盖用户级是闪退根因，必须暴露）。</summary>
    [Fact]
    public void Format_ShowsProfileOverrideChain()
    {
        var ex = new ConfigurationException("Provider 'openai' 配置无效: 缺少 API Key");

        var text = StartupErrorPrinter.Format(ex, FakeProbe(user: "zhipu", project: "openai"));

        Assert.Contains("zhipu", text);
        Assert.Contains("openai", text);
        Assert.Contains("项目级", text);
        Assert.Contains("用户级", text);
    }

    /// <summary>修复建议 — 三条路径（改指针/补 key/环境变量）+ auth.json 路径。</summary>
    [Fact]
    public void Format_ContainsFixGuidanceAndAuthPath()
    {
        var ex = new ConfigurationException("Provider 'openai' 配置无效: 缺少 API Key");

        var text = StartupErrorPrinter.Format(ex, FakeProbe());

        Assert.Contains("auth.json", text);
        Assert.Contains("OPENAI_API_KEY", text);
        Assert.Contains("current.profile", text);
    }

    /// <summary>key 有效时 — 不误导用户补 key（auth 有值场景提示指向检查）。</summary>
    [Fact]
    public void Format_WithValidAuthKey_PointsToPointer()
    {
        var ex = new ConfigurationException("Provider 'openai' 配置无效: 缺少 API Key");

        var text = StartupErrorPrinter.Format(ex, FakeProbe(authLength: 51));

        Assert.Contains("auth.json", text);
    }

    /// <summary>探测兜底 — 文件缺失/解析失败不抛异常（probe 自身容错，探测失败返回 null 字段）。</summary>
    [Fact]
    public void Probe_MissingFiles_ReturnsNullFieldsWithoutThrow()
    {
        var probe = StartupErrorPrinter.Probe("openai", userSettingsJson: null, projectSettingsJson: null, authJson: null, envValue: null);

        Assert.Null(probe.UserProfile);
        Assert.Null(probe.ProjectProfile);
        Assert.Equal(0, probe.AuthKeyLength);
    }

    /// <summary>探测解析 — 用户级/项目级 settings.json + auth.json 键长度 + 环境变量名。</summary>
    [Fact]
    public void Probe_ParsesSettingsAndAuth()
    {
        var settings = """{"current":{"profile":"sensenova"}}""";
        var project = """{"current":{"profile":"zhipu"}}""";
        var auth = """{"sensenova":"sk-0123456789abcdef0123456789abcdef"}""";

        var probe = StartupErrorPrinter.Probe("sensenova", settings, project, auth, envValue: null);

        Assert.Equal("sensenova", probe.UserProfile);
        Assert.Equal("zhipu", probe.ProjectProfile);
        Assert.Equal(35, probe.AuthKeyLength);
        Assert.Equal("SENSENOVA_API_KEY", probe.EnvVarName);
        Assert.Null(probe.EnvVarValue);
    }

    /// <summary>探测容错 — 非法 JSON 不抛（解析失败降级 null）。</summary>
    [Fact]
    public void Probe_InvalidJson_DegradesToNull()
    {
        var probe = StartupErrorPrinter.Probe("openai", userSettingsJson: "{broken", projectSettingsJson: "not-json", authJson: "[", envValue: null);

        Assert.Null(probe.UserProfile);
        Assert.Null(probe.ProjectProfile);
        Assert.Equal(0, probe.AuthKeyLength);
    }
}
