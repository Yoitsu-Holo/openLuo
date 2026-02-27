namespace openLuo.Modules.Agent.Application;

public enum AgentConversationRole
{
    User = 0,
    Assistant = 1
}

public sealed record AgentConversationMessage(AgentConversationRole Role, string Content);
