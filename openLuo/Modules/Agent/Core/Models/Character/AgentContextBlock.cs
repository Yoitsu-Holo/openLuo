using openLuo.Modules.Llm.Core.Models;

namespace openLuo.Modules.Agent.Application;

public sealed record AgentContextBlock(EnhanceMessageRule Rule, string Content);
