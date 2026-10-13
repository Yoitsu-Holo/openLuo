namespace openLuo.AgentContext.Core;

/// <summary>
/// 文本 → token 估算端口（D44）。上下文预算是唯一需要它的地方：
/// 装配器用它度量系统块，会话用它度量对话历史；贡献者**不得**自己估算 token
/// （各自拍脑袋会导致预算失真，见 ContextContribution 内注释）。
/// 宿主可注册真 tokenizer（如与所选模型配套的分词器）覆盖默认启发式实现。
/// </summary>
public interface ITokenCounter
{
    /// <summary>估算 <paramref name="text"/> 的 token 数；null/空串为 0。实现必须纯函数、无副作用。</summary>
    int Count(string? text);
}
