namespace JoinCode.Abstractions.Configuration;

/// <summary>
/// HTTP 客户端模式枚举 — 通过 JCC_HTTP_MODE 环境变量一键切换
/// Real=真实网络（默认）, Mock=拦截请求返回预设响应（调试/E2E测试用）
/// </summary>
public enum HttpMode : byte
{
    [EnumValue("Real")][DisplayText("真实网络")] Real,
    [EnumValue("Mock")][DisplayText("模拟拦截")] Mock
}
