
namespace JoinCode.Abstractions.Decision;

/// <summary>
/// 类型化决策服务 — 与 IQueryService 平级,面向 System One 模型(Jev 等)
/// IQueryService 返回 ApiMessage(文本生成),本接口返回 TypedDecisionResult(类型化决策)
/// 实现方同时实现 IQueryService 以走 QueryServiceFactory 统一分派,本接口供需要强类型决策的消费方使用
/// </summary>
public interface ITypedDecisionService {
    /// <summary>
    /// 提交 state + questions,获取类型化决策结果
    /// </summary>
    /// <param name="state">输入材料(上下文),字符串/JSON 对象/数组的 JSON 表示</param>
    /// <param name="questions">具名问题字典,key 为问题标识,value 为问题定义</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>每个问题的决策结果,key 对应 questions 的 key</returns>
    Task<TypedDecisionResult> GetTypedDecisionsAsync(
        string state,
        IReadOnlyDictionary<string, TypedDecisionQuestion> questions,
        CancellationToken cancellationToken = default);
}
