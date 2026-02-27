using openLuo.Modules.Agent.Application;
using openLuo.Modules.InterAgent.Core.Interfaces;
using openLuo.Modules.InterAgent.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using openLuo.Core.Interfaces;
using System.Text;
using openLuo.Modules.AppShell.Application;

namespace openLuo.Modules.InterAgent.Application;

public sealed class InterAgentMessenger(
    IAgentDispatcher dispatcher,
    IAgentRoster roster,
    IServiceProvider services,
    IGameLogger? logger = null,
    IGameStreams? streams = null,
    IRuntimeConfigCenter? configCenter = null) : IInterAgentMessenger
{
    private int HiddenDialogueFuseTurns => Math.Max(1, configCenter?.GetSnapshot().InterAgent.HiddenDialogueFuseTurns ?? 24);
    private TimeSpan AskTimeout => TimeSpan.FromSeconds(Math.Max(1, configCenter?.GetSnapshot().InterAgent.AskTimeoutSeconds ?? 12));
    private TimeSpan SessionTurnTimeout => TimeSpan.FromSeconds(Math.Max(1, configCenter?.GetSnapshot().InterAgent.SessionTurnTimeoutSeconds ?? 12));

    public async Task<InterAgentAskResult> AskAsync(InterAgentAskRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.TargetSelector))
            return new InterAgentAskResult { Success = false, Error = "缺少目标角色。" };
        if (string.IsNullOrWhiteSpace(request.Question))
            return new InterAgentAskResult { Success = false, Error = "缺少问题内容。" };

        var targetCharacter = await roster.ResolveAsync(request.GameId, request.TargetSelector, ct);
        if (targetCharacter is null)
        {
            logger?.Warn("inter-agent", "ask target not found", new { request.GameId, request.FromCharacterId, request.TargetSelector });
            return new InterAgentAskResult
            {
                Success = false,
                Error = $"找不到目标角色：{request.TargetSelector}"
            };
        }

        var runtimeHub = services.GetRequiredService<IAgentRuntimeHub>();
        await runtimeHub.EnsurePartyStartedAsync(request.GameId, ct);
        logger?.Debug("inter-agent", "ask dispatch", new
        {
            request.GameId,
            from = request.FromCharacterId,
            to = targetCharacter.Id,
            target = targetCharacter.Name,
            request.Question
        });
        request.ExecutionContext?.ReportProgress($"inter_agent_ask_dispatch:{request.FromCharacterId}->{targetCharacter.Id}");

        var reply = request.ExecutionContext is null
            ? await runtimeHub.RequestAsync(
                characterId: targetCharacter.Id,
                type: AgentMessageType.AgentAsk,
                from: request.FromCharacterId,
                payload: BuildAskPayload(request.Question),
                gameId: request.GameId,
                correlationId: request.CorrelationId ?? $"agentask_{Guid.NewGuid():N}",
                timeout: AskTimeout,
                ct: ct)
            : await runtimeHub.RequestAsync(
                characterId: targetCharacter.Id,
                type: AgentMessageType.AgentAsk,
                from: request.FromCharacterId,
                payload: BuildAskPayload(request.Question),
                gameId: request.GameId,
                correlationId: request.CorrelationId ?? $"agentask_{Guid.NewGuid():N}",
                timeout: request.ExecutionContext.RemainingOverallTime,
                executionContext: request.ExecutionContext,
                ct: ct);

        if (reply is null)
        {
            logger?.Warn("inter-agent", "ask reply timeout", new { request.GameId, from = request.FromCharacterId, to = targetCharacter.Id, target = targetCharacter.Name });
            return new InterAgentAskResult
            {
                Success = false,
                TargetCharacterId = targetCharacter.Id,
                TargetDisplayName = targetCharacter.Name,
                Error = $"未收到 {targetCharacter.Name} 的回复。"
            };
        }

        if (reply.PendingAbility is not null)
        {
            logger?.Warn("inter-agent", "ask reply pending ability", new { request.GameId, from = request.FromCharacterId, to = targetCharacter.Id, target = targetCharacter.Name });
            return new InterAgentAskResult
            {
                Success = false,
                TargetCharacterId = targetCharacter.Id,
                TargetDisplayName = targetCharacter.Name,
                Error = $"{targetCharacter.Name} 的回复进入了待确认状态，当前 ask_character 不支持继续链式确认。"
            };
        }

        if (string.IsNullOrWhiteSpace(reply.Payload))
        {
            logger?.Warn("inter-agent", "ask reply empty", new { request.GameId, from = request.FromCharacterId, to = targetCharacter.Id, target = targetCharacter.Name });
            return new InterAgentAskResult
            {
                Success = false,
                TargetCharacterId = targetCharacter.Id,
                TargetDisplayName = targetCharacter.Name,
                Error = $"{targetCharacter.Name} 没有返回有效内容。"
            };
        }

        logger?.Debug("inter-agent", "ask reply ok", new
        {
            request.GameId,
            from = request.FromCharacterId,
            to = targetCharacter.Id,
            target = targetCharacter.Name,
            endDialogue = reply.EndDialogue
        });
        request.ExecutionContext?.ReportProgress($"inter_agent_ask_reply:{request.FromCharacterId}->{targetCharacter.Id}");
        return new InterAgentAskResult
        {
            Success = true,
            TargetCharacterId = targetCharacter.Id,
            TargetDisplayName = targetCharacter.Name,
            Reply = reply.Payload.Trim(),
            Outcome = reply.InterAgentOutcome ?? new InterAgentOutcome
            {
                ReplyText = reply.Payload.Trim()
            }
        };
    }

    public async Task<InterAgentChatSessionResult> ChatSessionAsync(InterAgentChatSessionRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.TargetSelector))
            return new InterAgentChatSessionResult { Success = false, Error = "缺少目标角色。" };
        if (string.IsNullOrWhiteSpace(request.Opening))
            return new InterAgentChatSessionResult { Success = false, Error = "缺少开场白。" };

        var allCharacters = await roster.ListAsync(request.GameId, ct);
        var fromCharacter = allCharacters.FirstOrDefault(x =>
            string.Equals(x.Id, request.FromCharacterId, StringComparison.OrdinalIgnoreCase));
        var targetCharacter = await roster.ResolveAsync(request.GameId, request.TargetSelector, ct);
        if (fromCharacter is null)
        {
            logger?.Warn("inter-agent", "session source not found", new { request.GameId, request.FromCharacterId, request.TargetSelector });
            return new InterAgentChatSessionResult { Success = false, Error = $"找不到发起角色：{request.FromCharacterId}" };
        }
        if (targetCharacter is null)
        {
            logger?.Warn("inter-agent", "session target not found", new { request.GameId, request.FromCharacterId, request.TargetSelector });
            return new InterAgentChatSessionResult { Success = false, Error = $"找不到目标角色：{request.TargetSelector}" };
        }

        var runtimeHub = services.GetRequiredService<IAgentRuntimeHub>();
        await runtimeHub.EnsurePartyStartedAsync(request.GameId, ct);
        logger?.Debug("inter-agent", "session start", new
        {
            request.GameId,
            from = fromCharacter.Id,
            fromName = fromCharacter.Name,
            to = targetCharacter.Id,
            targetName = targetCharacter.Name,
            request.Opening
        });

        var transcript = new List<InterAgentDialogueTurn>
        {
            new()
            {
                SpeakerCharacterId = fromCharacter.Id,
                SpeakerDisplayName = fromCharacter.Name,
                Content = request.Opening.Trim()
            }
        };
        StreamTranscriptTurn(fromCharacter.Name, request.Opening);

        var currentSpeaker = fromCharacter;
        var currentTarget = targetCharacter;
        var currentUtterance = BuildDialoguePayload(request.Opening);
        var correlationId = request.CorrelationId ?? $"agentchat_{Guid.NewGuid():N}";

        for (var turn = 0; turn < HiddenDialogueFuseTurns; turn++)
        {
            logger?.Debug("inter-agent", "session turn dispatch", new
            {
                request.GameId,
                turn,
                from = currentSpeaker.Id,
                fromName = currentSpeaker.Name,
                    to = currentTarget.Id,
                    toName = currentTarget.Name,
                    payload = currentUtterance
                });
            AgentMessage? reply;
            try
            {
                using var turnCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                turnCts.CancelAfter(SessionTurnTimeout);
                reply = await runtimeHub.RequestAsync(
                    characterId: currentTarget.Id,
                    type: AgentMessageType.AgentDialogueTurn,
                    from: currentSpeaker.Id,
                    payload: currentUtterance,
                    gameId: request.GameId,
                    correlationId: correlationId,
                    timeout: SessionTurnTimeout,
                    executionContext: request.ExecutionContext,
                    ct: turnCts.Token);
            }
            catch (OperationCanceledException)
            {
                reply = null;
            }

            if (reply is null)
            {
                logger?.Warn("inter-agent", "session turn timeout", new
                {
                    request.GameId,
                    turn,
                    from = currentSpeaker.Id,
                    to = currentTarget.Id,
                    toName = currentTarget.Name
                });
                return new InterAgentChatSessionResult
                {
                    Success = false,
                    TargetCharacterId = targetCharacter.Id,
                    TargetDisplayName = targetCharacter.Name,
                    Error = $"未收到 {currentTarget.Name} 的对话回复。",
                    Transcript = transcript
                };
            }

            if (reply.PendingAbility is not null)
            {
                logger?.Warn("inter-agent", "session turn pending ability", new
                {
                    request.GameId,
                    turn,
                    from = currentSpeaker.Id,
                    to = currentTarget.Id,
                    toName = currentTarget.Name
                });
                return new InterAgentChatSessionResult
                {
                    Success = false,
                    TargetCharacterId = targetCharacter.Id,
                    TargetDisplayName = targetCharacter.Name,
                    Error = $"{currentTarget.Name} 的对话回复进入了待确认状态，当前 chat session 不支持链式确认。",
                    Transcript = transcript
                };
            }

            if (string.IsNullOrWhiteSpace(reply.Payload))
            {
                logger?.Warn("inter-agent", "session turn empty reply", new
                {
                    request.GameId,
                    turn,
                    from = currentSpeaker.Id,
                    to = currentTarget.Id,
                    toName = currentTarget.Name
                });
                return new InterAgentChatSessionResult
                {
                    Success = false,
                    TargetCharacterId = targetCharacter.Id,
                    TargetDisplayName = targetCharacter.Name,
                    Error = $"{currentTarget.Name} 没有返回有效对话内容。",
                    Transcript = transcript
                };
            }

            transcript.Add(new InterAgentDialogueTurn
            {
                SpeakerCharacterId = currentTarget.Id,
                SpeakerDisplayName = currentTarget.Name,
                Content = reply.Payload.Trim()
            });
            StreamTranscriptTurn(currentTarget.Name, reply.Payload);
            logger?.Debug("inter-agent", "session turn reply", new
            {
                request.GameId,
                turn,
                speaker = currentTarget.Id,
                speakerName = currentTarget.Name,
                endDialogue = reply.EndDialogue,
                payload = reply.Payload
            });

            if (reply.EndDialogue)
            {
                logger?.Info("inter-agent", "session end by agent", new
                {
                    request.GameId,
                    turn,
                    speaker = currentTarget.Id,
                    speakerName = currentTarget.Name,
                    transcriptCount = transcript.Count
                });
                return new InterAgentChatSessionResult
                {
                    Success = true,
                    TargetCharacterId = targetCharacter.Id,
                    TargetDisplayName = targetCharacter.Name,
                    Transcript = transcript
                };
            }

            (currentSpeaker, currentTarget) = (currentTarget, currentSpeaker);
            currentUtterance = BuildDialoguePayload(reply.Payload);
        }

        logger?.Warn("inter-agent", "session fuse reached", new
        {
            request.GameId,
            from = fromCharacter.Id,
            to = targetCharacter.Id,
            transcriptCount = transcript.Count,
            fuseTurns = HiddenDialogueFuseTurns
        });
        return new InterAgentChatSessionResult
        {
            Success = true,
            TargetCharacterId = targetCharacter.Id,
            TargetDisplayName = targetCharacter.Name,
            Transcript = transcript
        };
    }

    private static string BuildAskPayload(string question)
    {
        return string.Join("\n", [
            "内部咨询：请用 1 到 3 句、符合你角色设定的自然口吻回答，供另一位角色转述给玩家。",
            "不要自称 AI、助手、系统，也不要讲任务或安全策略。",
            $"问题：{question}"
        ]);
    }

    private static string BuildDialoguePayload(string utterance)
    {
        return string.Join("\n", [
            "内部对话：你正在和另一位角色聊天。",
            "请像自然聊天一样只回复一小段话。",
            "如果你觉得这段对话说到这里就自然结束，请在这轮结束对话。",
            $"对方刚刚说：{utterance.Trim()}"
        ]);
    }

    private void StreamTranscriptTurn(string speakerName, string content)
    {
        if (streams is null || string.IsNullOrWhiteSpace(content))
            return;

        var line = $"{speakerName}：{content.Trim()}\n";
        var bytes = Encoding.UTF8.GetBytes(line);
        streams.Output.Write(bytes);
        streams.Output.Flush();
    }

}
