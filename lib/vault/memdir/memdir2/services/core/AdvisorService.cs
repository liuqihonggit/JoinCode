namespace Core.Memdir;

/// <summary>
/// 顾问模型服务 — 管理当前选定的顾问模型 ID，持久化到配置文件
/// </summary>
[Register(typeof(ConfigPersistentServiceBase<string>), ServiceLifetime.Singleton)]
[Register(typeof(IAdvisorService), ServiceLifetime.Singleton)]
public sealed partial class AdvisorService : ConfigPersistentServiceBase<string>, IAdvisorService {
    private const string NoneValue = "";

    /// <summary>
    /// 构造顾问模型服务
    /// </summary>
    /// <param name="configService">配置服务（可选，用于持久化）</param>
    public AdvisorService(IConfigurationService? configService = null)
        : base(NoneValue, configService) { }

    /// <summary>
    /// 配置键名 — "advisor.model"
    /// </summary>
    protected override string ConfigKey => "advisor.model";
    /// <summary>
    /// 尝试将原始配置字符串解析为顾问模型 ID
    /// </summary>
    /// <param name="raw">原始配置值</param>
    /// <param name="result">解析结果</param>
    /// <returns>解析是否成功</returns>
    protected override bool TryParseConfigValue(string? raw, out string result) {
        if (!string.IsNullOrEmpty(raw)) {
            result = raw;
            return true;
        }
        result = NoneValue;
        return false;
    }
    /// <summary>
    /// 将顾问模型 ID 格式化为配置字符串
    /// </summary>
    /// <param name="value">顾问模型 ID</param>
    /// <returns>格式化后的配置字符串</returns>
    protected override string FormatConfigValue(string value) => value;

    /// <summary>
    /// 当前顾问模型 ID，未设置时返回 null
    /// </summary>
    public string? AdvisorModel {
        get {
            var v = Value;
            return v == NoneValue ? null : v;
        }
    }

    /// <summary>
    /// 顾问是否已启用（已设置顾问模型）
    /// </summary>
    public bool IsAdvisorEnabled => Value != NoneValue;

    /// <summary>
    /// 设置顾问模型 ID
    /// </summary>
    /// <param name="modelId">模型 ID</param>
    public void SetAdvisorModel(string modelId) => SetValue(modelId);

    /// <summary>
    /// 清除顾问模型设置
    /// </summary>
    public void ClearAdvisorModel() => SetValue(NoneValue);
}