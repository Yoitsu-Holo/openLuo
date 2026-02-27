namespace openLuo.Modules.Agent.Application;

public static class CharacterTurnPolicy
{
    public static bool ShouldUsePrivateMemory(AgentMessageType messageType) =>
        !IsInternalLoopMessage(messageType);

    public static bool ShouldUseSharedMemory(AgentMessageType messageType) =>
        !IsInternalLoopMessage(messageType);

    public static bool IsInternalLoopMessage(AgentMessageType messageType) =>
        messageType is AgentMessageType.ToolResult
            or AgentMessageType.System
            or AgentMessageType.AgentAsk
            or AgentMessageType.AgentReply
            or AgentMessageType.AgentDialogueTurn;
}
