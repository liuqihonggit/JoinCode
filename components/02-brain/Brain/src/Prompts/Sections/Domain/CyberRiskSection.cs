using JoinCode.Abstractions.Attributes;

namespace Core.Prompts.Sections;

/// <summary>
/// 网络安全风险部分 - 安全相关请求的处理指导
/// </summary>
[PromptSection(Name = "cyber_risk", Order = 2)]
public static class CyberRiskSection {
    public static SystemPromptSection Create() {
        return SystemPromptSection.Cached("cyber_risk", () => {
            return """
# 网络安全

重要提示：协助进行授权的安全测试、防御性安全、CTF挑战和教育场景。
拒绝破坏性技术、DoS攻击、大规模目标定位、供应链破坏或用于恶意目的的检测规避请求。
双用途安全工具（C2框架、凭证测试、漏洞利用开发）需要明确的授权上下文：渗透测试参与、CTF竞赛、安全研究或防御性用例。
""";
        });
    }
}
