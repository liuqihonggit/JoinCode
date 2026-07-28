namespace Core.Memdir;

[Register]
public sealed partial class AdvisorService : ConfigPersistentServiceBase<string>, IAdvisorService
{
    private const string NoneValue = "";

    public AdvisorService(IConfigurationService? configService = null)
        : base(NoneValue, configService) { }

    protected override string ConfigKey => "advisor.model";
    protected override bool TryParseConfigValue(string? raw, out string result)
    {
        if (!string.IsNullOrEmpty(raw))
        {
            result = raw;
            return true;
        }
        result = NoneValue;
        return false;
    }
    protected override string FormatConfigValue(string value) => value;

    public string? AdvisorModel
    {
        get
        {
            var v = Value;
            return v == NoneValue ? null : v;
        }
    }

    public bool IsAdvisorEnabled => Value != NoneValue;

    public void SetAdvisorModel(string modelId) => SetValue(modelId);

    public void ClearAdvisorModel() => SetValue(NoneValue);
}
