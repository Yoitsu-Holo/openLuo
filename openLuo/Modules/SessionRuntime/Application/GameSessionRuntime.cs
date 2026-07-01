using openLuo.Core.Interfaces;
using openLuo.Core.Models;
using openLuo.Infrastructure.Database;
using openLuo.Modules.Agent.Application;
using openLuo.Modules.Assets.Core.Interfaces;
using openLuo.Modules.Commanding.Core.Models;
using openLuo.Modules.Content.Application.Registry;
using openLuo.Modules.Content.Core.Definitions;
using openLuo.Modules.PluginRuntime.Core.Interfaces;
using openLuo.Modules.SessionRuntime.Core.Interfaces;
using openLuo.Modules.SessionRuntime.Core.Models;

namespace openLuo.Modules.SessionRuntime.Application;

public sealed class GameSessionRuntime : IGameSessionRuntime
{
    private readonly ISessionRegistry _sessionRegistry;
    private readonly IOutputEventBus _outputEventBus;
    private readonly IInputContentStore _inputContentStore;
    private readonly IAttachmentAssetBridge _attachmentAssetBridge;
    private readonly IInputRouter _inputRouter;
    private readonly ISessionExecutionContextAccessor _executionContextAccessor;
    private readonly IGameContextAccessor _gameContextAccessor;
    private readonly IAssetStore _assetStore;
    private readonly IAssetBlobStore _assetBlobStore;
    private readonly IGameEngine _gameEngine;
    private readonly IAgentRuntimeHub _runtimeHub;
    private readonly DatabaseInitializer _databaseInitializer;
    private readonly IPluginHost _pluginHost;
    private readonly ISessionBootstrapper _sessionBootstrapper;
    private readonly ContentRegistry _contentRegistry;
    private readonly ICharacterRepository _characterRepository;
    private readonly IStatusAggregator _statusAggregator;
    private readonly IGameStateRepository _gameStateRepository;
    private readonly IAgentContextStore _agentContextStore;
    private readonly string _pluginsDir;
    private readonly SemaphoreSlim _startupLock = new(1, 1);
    private bool _startupComplete;

    public GameSessionRuntime(
        ISessionRegistry sessionRegistry,
        IOutputEventBus outputEventBus,
        IInputContentStore inputContentStore,
        IAttachmentAssetBridge attachmentAssetBridge,
        IInputRouter inputRouter,
        ISessionExecutionContextAccessor executionContextAccessor,
        IGameContextAccessor gameContextAccessor,
        IAssetStore assetStore,
        IAssetBlobStore assetBlobStore,
        IGameEngine gameEngine,
        IAgentRuntimeHub runtimeHub,
        DatabaseInitializer databaseInitializer,
        IPluginHost pluginHost,
        ISessionBootstrapper sessionBootstrapper,
        ContentRegistry contentRegistry,
        ICharacterRepository characterRepository,
        IStatusAggregator statusAggregator,
        IGameStateRepository gameStateRepository,
        IAgentContextStore agentContextStore,
        string baseDir)
    {
        _sessionRegistry = sessionRegistry;
        _outputEventBus = outputEventBus;
        _inputContentStore = inputContentStore;
        _attachmentAssetBridge = attachmentAssetBridge;
        _inputRouter = inputRouter;
        _executionContextAccessor = executionContextAccessor;
        _gameContextAccessor = gameContextAccessor;
        _assetStore = assetStore;
        _assetBlobStore = assetBlobStore;
        _gameEngine = gameEngine;
        _runtimeHub = runtimeHub;
        _databaseInitializer = databaseInitializer;
        _pluginHost = pluginHost;
        _sessionBootstrapper = sessionBootstrapper;
        _contentRegistry = contentRegistry;
        _characterRepository = characterRepository;
        _statusAggregator = statusAggregator;
        _gameStateRepository = gameStateRepository;
        _agentContextStore = agentContextStore;
        _pluginsDir = Path.Combine(baseDir, "data", "plugins");
    }

    public async Task<SessionHandle> OpenAsync(SessionOpenRequest request, CancellationToken ct = default)
    {
        await EnsureStartupAsync(ct);
        var handle = _sessionRegistry.Create(request);
        var hasArchetypeId = request.Metadata.TryGetValue("archetypeId", out var archetypeId) &&
            !string.IsNullOrWhiteSpace(archetypeId);
        if (hasArchetypeId)
        {
            var playerName = request.Metadata.TryGetValue("playerName", out var seededPlayerName) && !string.IsNullOrWhiteSpace(seededPlayerName)
                ? seededPlayerName
                : "玩家";
            await InitGameAsync(handle.SessionId, archetypeId!, playerName, request.PreferredGameId, ct);
        }
        GameState? state = null;
        var previousGameContext = _gameContextAccessor.Current;
        try
        {
            state = await TryGetStateAsync(handle.SessionId, ct);
            if (state is not null)
                await _runtimeHub.EnsurePartyStartedAsync(state.Id, ct);
            _gameContextAccessor.Current = state is null ? null : new GameRuntimeContext { GameId = state.Id };
            await _outputEventBus.PublishAsync(new SessionStateEvent
            {
                SessionId = handle.SessionId,
                ChannelId = "system",
                EventId = Guid.NewGuid().ToString("N"),
                Kind = GameEventKind.SessionState,
                State = state is null ? "opened:no-game" : "opened:ready",
                GameId = state?.Id
            }, ct);
            await PublishStatusSnapshotAsync(handle.SessionId, "system", state, ct);
        }
        finally
        {
            _gameContextAccessor.Current = previousGameContext;
        }
        return handle;
    }

    public Task CloseAsync(string sessionId, CancellationToken ct = default)
    {
        _sessionRegistry.Remove(sessionId);
        _outputEventBus.Complete(sessionId);
        return Task.CompletedTask;
    }

    public IAsyncEnumerable<GameEvent> StreamEventsAsync(string sessionId, CancellationToken ct = default)
    {
        EnsureSessionExists(sessionId);
        return _outputEventBus.StreamAsync(sessionId, ct);
    }

    public async Task<IReadOnlyList<SessionGameEntry>> GetGameIdsAsync(CancellationToken ct = default)
    {
        await EnsureStartupAsync(ct);
        var states = await _gameStateRepository.ListAsync(ct);
        return states
            .Select(state => new SessionGameEntry
            {
                GameId = state.Id,
                PlayerName = state.PlayerName,
                ArchetypeId = state.ArchetypeId,
                UpdatedAtUtc = DateTime.SpecifyKind(state.UpdatedAt, DateTimeKind.Utc)
            })
            .ToList();
    }

    public async Task<GameState?> TryGetStateAsync(string sessionId, CancellationToken ct = default)
    {
        EnsureSessionExists(sessionId);
        var gameId = ResolveSessionGameId(sessionId);
        if (string.IsNullOrWhiteSpace(gameId))
            return null;

        try
        {
            return await _gameEngine.GetStateAsync(gameId, ct);
        }
        catch
        {
            return null;
        }
    }

    public Task<IReadOnlyList<SessionArchetypeOption>> GetAvailableArchetypesAsync(CancellationToken ct = default)
    {
        IReadOnlyList<SessionArchetypeOption> options = _contentRegistry.GetAll<CharacterArchetypeDefinition>()
            .Select(bg => new SessionArchetypeOption
            {
                Id = bg.Id,
                Name = bg.DisplayName,
                CharacterName = string.IsNullOrWhiteSpace(bg.CharacterName) ? bg.DisplayName : bg.CharacterName
            })
            .ToList();
        return Task.FromResult(options);
    }

    public async Task<IReadOnlyList<SessionCharacterRosterItem>> GetSessionRosterAsync(string sessionId, CancellationToken ct = default)
    {
        EnsureSessionExists(sessionId);
        var state = await TryGetStateAsync(sessionId, ct);
        if (state is null)
            return [];

        var characters = await _characterRepository.ListByGameIdAsync(state.Id, ct);
        return characters
            .OrderBy(c => c.DisplayPriority)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Select(c => new SessionCharacterRosterItem
            {
                CharacterId = c.Id,
                ArchetypeId = c.ArchetypeId,
                Name = c.Name,
                DisplayPriority = c.DisplayPriority,
                IsEnabled = c.IsEnabled,
                IsActive = string.Equals(c.Id, state.ActiveCharacterId, StringComparison.OrdinalIgnoreCase)
            })
            .ToList();
    }

    public async Task<SessionCharacterStatusSnapshot?> GetCharacterStatusSnapshotAsync(string sessionId, string characterId, CancellationToken ct = default)
    {
        EnsureSessionExists(sessionId);
        var state = await TryGetStateAsync(sessionId, ct);
        if (state is null)
            return null;

        var character = await _characterRepository.GetByIdAsync(state.Id, characterId, ct);
        if (character is null)
            return null;

        var status = await _statusAggregator.GetStatusAsync(state.Id, character.Id, character.ArchetypeId);
        return new SessionCharacterStatusSnapshot
        {
            CharacterId = character.Id,
            ArchetypeId = character.ArchetypeId,
            CharacterName = character.Name,
            CurrentDay = state.CurrentDay,
            CurrentMinute = state.CurrentMinute,
            IsActive = string.Equals(state.ActiveCharacterId, character.Id, StringComparison.OrdinalIgnoreCase),
            Items = status.Items,
            AdditionalText = status.AdditionalText
        };
    }

    public Task<IReadOnlyList<SessionAttachment>> GetAttachmentsAsync(string sessionId, CancellationToken ct = default)
    {
        EnsureSessionExists(sessionId);
        return _inputContentStore.ListAsync(sessionId, ct);
    }

    public Task<SessionAttachmentPayload?> GetAttachmentAsync(string sessionId, string attachmentId, CancellationToken ct = default)
    {
        EnsureSessionExists(sessionId);
        return _inputContentStore.GetAsync(sessionId, attachmentId, ct);
    }

    public async Task<SessionAssetDescriptor?> GetAssetDescriptorAsync(string sessionId, string assetId, CancellationToken ct = default)
    {
        EnsureSessionExists(sessionId);
        var record = await _assetStore.GetByIdAsync(assetId);
        if (record is null)
            return null;

        var blobs = await _assetBlobStore.GetInfoByAssetIdAsync(assetId);
        return new SessionAssetDescriptor
        {
            AssetId = record.Id,
            AssetType = record.AssetType,
            Namespace = record.Namespace,
            Label = record.Label,
            BlobInfos = blobs.Select(b => new SessionAssetBlobInfo
            {
                BlobId = b.Id,
                MimeType = b.MimeType,
                BlobRole = b.BlobRole,
                IsPrimary = b.IsPrimary,
                SizeBytes = b.SizeBytes,
                Sha256 = b.Sha256
            }).ToList()
        };
    }

    public async Task<SessionAssetBlob?> GetAssetBlobAsync(string sessionId, string assetId, string blobRole = "primary", CancellationToken ct = default)
    {
        EnsureSessionExists(sessionId);
        var blobs = await _assetBlobStore.GetInfoByAssetIdAsync(assetId);
        var target = blobs.FirstOrDefault(b =>
            string.Equals(b.BlobRole, blobRole, StringComparison.OrdinalIgnoreCase))
            ?? blobs.FirstOrDefault(b => b.IsPrimary)
            ?? blobs.FirstOrDefault();

        if (target is null)
            return null;

        var data = await _assetBlobStore.GetDataAsync(target.Id);
        if (data is null)
            return null;

        return new SessionAssetBlob
        {
            AssetId = assetId,
            BlobId = target.Id,
            MimeType = target.MimeType,
            Data = data
        };
    }

    public Task InitializeGameAsync(string sessionId, string archetypeId, string playerName, CancellationToken ct = default) =>
        InitGameAsync(sessionId, archetypeId, playerName, null, ct);

    public async Task<string> InitGameAsync(string sessionId, string archetypeId, string playerName, string? requestedGameId = null, CancellationToken ct = default)
    {
        EnsureSessionExists(sessionId);
        var existingGameId = ResolveSessionGameId(sessionId);
        if (!string.IsNullOrWhiteSpace(existingGameId))
        {
            var existingState = await _gameStateRepository.GetAsync(existingGameId, ct);
            if (existingState is not null)
            {
                await _runtimeHub.EnsurePartyStartedAsync(existingState.Id, ct);
                await PublishStatusSnapshotAsync(sessionId, "system", existingState, ct);
                return existingState.Id;
            }
        }

        var targetGameId = string.IsNullOrWhiteSpace(requestedGameId) ? sessionId : requestedGameId.Trim();
        var previousGameContext = _gameContextAccessor.Current;
        var gameId = targetGameId;
        try
        {
            gameId = await _gameEngine.InitializeAsync(targetGameId, archetypeId, playerName, ct);
            _sessionRegistry.BindGameId(sessionId, gameId);
            _gameContextAccessor.Current = new GameRuntimeContext { GameId = gameId };

            var state = await _gameEngine.GetStateAsync(gameId, ct);
            await _runtimeHub.EnsurePartyStartedAsync(state.Id, ct);
            await _outputEventBus.PublishAsync(new SessionStateEvent
            {
                SessionId = sessionId,
                ChannelId = "system",
                EventId = Guid.NewGuid().ToString("N"),
                Kind = GameEventKind.SessionState,
                State = "initialized",
                GameId = state.Id
            }, ct);
            await PublishStatusSnapshotAsync(sessionId, "system", state, ct);
        }
        finally
        {
            _gameContextAccessor.Current = previousGameContext;
        }
        return gameId;
    }

    public async Task<bool> SetActiveCharacterAsync(string sessionId, string characterId, CancellationToken ct = default)
    {
        EnsureSessionExists(sessionId);
        var state = await TryGetStateAsync(sessionId, ct);
        if (state is null)
            return false;

        var candidate = await _characterRepository.GetByIdAsync(state.Id, characterId, ct);
        if (candidate is null)
            return false;

        if (string.Equals(state.ActiveCharacterId, candidate.Id, StringComparison.OrdinalIgnoreCase))
            return true;

        state.ActiveCharacterId = candidate.Id;
        await _gameStateRepository.SaveAsync(state, ct);
        await PublishStatusSnapshotAsync(sessionId, "system", state, ct);
        return true;
    }

    public async Task<SessionSubmitResult> SubmitAsync(SessionInput input, CancellationToken ct = default)
    {
        EnsureSessionExists(input.SessionId);
        var nonTextParts = input.Parts.Where(p => p.Kind != SessionContentKind.Text).ToList();
        var attachmentRefs = new List<SessionAttachmentReference>();

        foreach (var part in nonTextParts)
        {
            var attachment = await _inputContentStore.PutAsync(input.SessionId, part, ct);
            var state = await TryGetStateAsync(input.SessionId, ct);
            if (state is not null)
            {
                var payload = await _inputContentStore.GetAsync(input.SessionId, attachment.AttachmentId, ct);
                if (payload is not null)
                {
                    var assetId = await _attachmentAssetBridge.CreateAssetForAttachmentAsync(
                        payload,
                        state.Id,
                        input.SourceId,
                        input.ChannelId,
                        ct);
                    if (!string.IsNullOrWhiteSpace(assetId))
                        attachment = await _inputContentStore.LinkAssetAsync(input.SessionId, attachment.AttachmentId, assetId, ct) ?? attachment;
                }
            }
            var attachmentRef = ToReference(attachment);
            attachmentRefs.Add(attachmentRef);
            await _outputEventBus.PublishAsync(new AttachmentAcceptedEvent
            {
                SessionId = input.SessionId,
                ChannelId = input.ChannelId,
                EventId = Guid.NewGuid().ToString("N"),
                Kind = GameEventKind.AttachmentAccepted,
                AttachmentId = attachment.AttachmentId,
                ContentKind = attachment.Kind,
                Name = attachment.Name,
                MediaType = attachment.MediaType,
                SizeBytes = attachment.SizeBytes,
                AssetId = attachment.AssetId
            }, ct);
        }

        if (nonTextParts.Count > 0)
        {
            var attachmentKinds = string.Join(", ", nonTextParts
                .Select(p => p.Kind.ToString())
                .Distinct(StringComparer.OrdinalIgnoreCase));
            await _outputEventBus.PublishAsync(new SystemNoticeEvent
            {
                SessionId = input.SessionId,
                ChannelId = input.ChannelId,
                EventId = Guid.NewGuid().ToString("N"),
                Kind = GameEventKind.SystemNotice,
                Notice = $"收到 {nonTextParts.Count} 个非文本输入片段（{attachmentKinds}）。"
            }, ct);
        }

        if (input.Kind == SessionInputKind.Ambient)
        {
            await RecordAmbientInputAsync(input, attachmentRefs, ct);
            return new SessionSubmitResult
            {
                Events = _outputEventBus.Drain(input.SessionId)
            };
        }

        if (string.IsNullOrWhiteSpace(input.Text) &&
            input.Command is null &&
            !input.Parts.Any(p => p.Kind == SessionContentKind.Text && !string.IsNullOrWhiteSpace(p.Text)))
        {
            var hasBinaryAttachmentsForChat = input.Kind == SessionInputKind.Chat && attachmentRefs.Count > 0;
            if (!hasBinaryAttachmentsForChat)
            {
                await _outputEventBus.PublishAsync(new ErrorEvent
                {
                    SessionId = input.SessionId,
                    ChannelId = input.ChannelId,
                    EventId = Guid.NewGuid().ToString("N"),
                    Kind = GameEventKind.Error,
                    Error = nonTextParts.Count > 0
                        ? "当前会话运行时已接收到非文本输入，但现阶段还不能在没有文本指令的情况下处理文件或二进制内容。"
                        : "当前只支持文本输入。"
                }, ct);
                return new SessionSubmitResult { Events = _outputEventBus.Drain(input.SessionId) };
            }
        }

        var executionRequest = await _inputRouter.RouteAsync(input, attachmentRefs, ct);
        if (executionRequest is null)
        {
            await _outputEventBus.PublishAsync(new ErrorEvent
            {
                SessionId = input.SessionId,
                ChannelId = input.ChannelId,
                EventId = Guid.NewGuid().ToString("N"),
                Kind = GameEventKind.Error,
                Error = "无法路由当前输入。"
            }, ct);
            return new SessionSubmitResult { Events = _outputEventBus.Drain(input.SessionId) };
        }

        var boundGameId = ResolveSessionGameId(input.SessionId);
        if (string.IsNullOrWhiteSpace(boundGameId))
        {
            await _outputEventBus.PublishAsync(new ErrorEvent
            {
                SessionId = input.SessionId,
                ChannelId = input.ChannelId,
                EventId = Guid.NewGuid().ToString("N"),
                Kind = GameEventKind.Error,
                Error = "当前 session 尚未绑定 gameId，请先初始化存档。"
            }, ct);
            return new SessionSubmitResult { Events = _outputEventBus.Drain(input.SessionId) };
        }

        executionRequest.Context.Metadata["gameId"] = boundGameId;
        var effectiveExecutionContext = new SessionExecutionContext
        {
            SessionId = executionRequest.Context.SessionId,
            GameId = boundGameId,
            SourceId = executionRequest.Context.SourceId,
            ChannelId = executionRequest.Context.ChannelId,
            ActorId = executionRequest.Context.ActorId,
            Attachments = executionRequest.Context.Attachments,
            InputKind = executionRequest.Context.InputKind,
            Origin = executionRequest.Context.Origin,
            PresentationProfile = executionRequest.Context.PresentationProfile,
            Metadata = executionRequest.Context.Metadata
        };

        await _outputEventBus.PublishAsync(new InputAcceptedEvent
        {
            SessionId = input.SessionId,
            ChannelId = input.ChannelId,
            EventId = Guid.NewGuid().ToString("N"),
            Kind = GameEventKind.InputAccepted,
            Visibility = OutputVisibility.Debug,
            RawInput = executionRequest.RawInput,
            Attachments = effectiveExecutionContext.Attachments
        }, ct);

        _executionContextAccessor.Current = effectiveExecutionContext;
        var previousGameContext = _gameContextAccessor.Current;

        // Fire execution as a background task so that events published by the flow
        // (AgentStep, TextOutput, etc.) flow to the frontend asynchronously.
        // We return as soon as the first TextOutputEvent or MessageEvent arrives.
        var executionTask = Task.Run(async () =>
        {
            CommandResult result;
            try
            {
                _gameContextAccessor.Current = new GameRuntimeContext { GameId = boundGameId };
                result = await _gameEngine.ExecuteAsync(boundGameId, executionRequest.RawInput, ct);
            }
            catch (Exception ex)
            {
                await _outputEventBus.PublishAsync(new ErrorEvent
                {
                    SessionId = input.SessionId,
                    ChannelId = input.ChannelId,
                    EventId = Guid.NewGuid().ToString("N"),
                    Kind = GameEventKind.Error,
                    Error = $"内部错误：{ex.Message}"
                }, CancellationToken.None);
                return;
            }
            finally
            {
                _executionContextAccessor.Current = null;
                _gameContextAccessor.Current = previousGameContext;
            }

            if (result.Success)
            {
                var streamedPublicOutput = result.Metadata.TryGetValue(CommandResultMetadataKeys.StreamedPublicOutput, out var streamedObj) &&
                                           streamedObj is bool streamedBool &&
                                           streamedBool;
                var effectivePresentation = result.Presentation.Messages.Count > 0
                    ? result.Presentation
                    : CommandPresentation.Empty;

                if (!streamedPublicOutput && effectivePresentation.Messages.Count > 0)
                {
                    foreach (var message in effectivePresentation.Messages)
                    {
                        await _outputEventBus.PublishAsync(new MessageEvent
                        {
                            SessionId = input.SessionId,
                            ChannelId = input.ChannelId,
                            EventId = Guid.NewGuid().ToString("N"),
                            Kind = GameEventKind.MessageOutput,
                            Visibility = message.Visibility,
                            MessageId = message.MessageId,
                            SpeakerRole = message.SpeakerRole,
                            SpeakerId = message.SpeakerId,
                            Blocks = message.Blocks
                        }, CancellationToken.None);
                    }
                }
                else if (!streamedPublicOutput)
                {
                    var textOutput = !string.IsNullOrWhiteSpace(result.Output)
                        ? result.Output
                        : effectivePresentation.ToPlainText();

                    if (!string.IsNullOrWhiteSpace(textOutput))
                    {
                        await _outputEventBus.PublishAsync(new TextOutputEvent
                        {
                            SessionId = input.SessionId,
                            ChannelId = input.ChannelId,
                            EventId = Guid.NewGuid().ToString("N"),
                            Kind = GameEventKind.TextOutput,
                            Visibility = OutputVisibility.Public,
                            Text = textOutput
                        }, CancellationToken.None);
                    }
                }
            }
            else
            {
                await _outputEventBus.PublishAsync(new ErrorEvent
                {
                    SessionId = input.SessionId,
                    ChannelId = input.ChannelId,
                    EventId = Guid.NewGuid().ToString("N"),
                    Kind = GameEventKind.Error,
                    Error = result.Error ?? "未知错误"
                }, CancellationToken.None);
            }

            var latestState = await TryGetStateAsync(input.SessionId, CancellationToken.None);
            await PublishStatusSnapshotAsync(input.SessionId, input.ChannelId, latestState, CancellationToken.None);
        }, ct);

        // Wait for the first content-bearing event (chat text is ready).
        var earlyEvents = await WaitForFirstTextOutputAsync(input.SessionId, executionTask, ct);
        return new SessionSubmitResult { Events = earlyEvents };
    }

    private async Task<IReadOnlyList<GameEvent>> WaitForFirstTextOutputAsync(
        string sessionId,
        Task executionTask,
        CancellationToken ct)
    {
        var events = new List<GameEvent>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        await foreach (var gameEvent in _outputEventBus.StreamAsync(sessionId, ct))
        {
            if (!seen.Add(gameEvent.EventId))
                continue;

            events.Add(gameEvent);

            if (gameEvent is TextOutputEvent or MessageEvent)
            {
                // Drain remaining queued events and merge.
                foreach (var remainingEvent in _outputEventBus.Drain(sessionId))
                {
                    if (seen.Add(remainingEvent.EventId))
                        events.Add(remainingEvent);
                }
                return events;
            }

            if (executionTask.IsCompleted)
            {
                foreach (var remainingEvent in _outputEventBus.Drain(sessionId))
                {
                    if (seen.Add(remainingEvent.EventId))
                        events.Add(remainingEvent);
                }
                return events;
            }
        }

        foreach (var e in _outputEventBus.Drain(sessionId))
        {
            if (seen.Add(e.EventId))
                events.Add(e);
        }
        return events;
    }

    private async Task EnsureStartupAsync(CancellationToken ct)
    {
        if (_startupComplete)
            return;

        await _startupLock.WaitAsync(ct);
        try
        {
            if (_startupComplete)
                return;

            await _databaseInitializer.InitializeAsync();
            await _pluginHost.LoadAllAsync(_pluginsDir, ct);
            _startupComplete = true;
        }
        finally
        {
            _startupLock.Release();
        }
    }

    private void EnsureSessionExists(string sessionId)
    {
        if (!_sessionRegistry.Exists(sessionId))
            throw new InvalidOperationException($"Session 不存在：{sessionId}");
    }

    private string? ResolveSessionGameId(string sessionId) =>
        _sessionRegistry.Get(sessionId)?.GameId;

    private static SessionAttachmentReference ToReference(SessionAttachment attachment) => new()
    {
        AttachmentId = attachment.AttachmentId,
        AssetId = attachment.AssetId,
        Kind = attachment.Kind,
        Name = attachment.Name,
        MediaType = attachment.MediaType,
        SizeBytes = attachment.SizeBytes,
        Sha256 = attachment.Sha256
    };

    private async Task PublishStatusSnapshotAsync(string sessionId, string channelId, GameState? state, CancellationToken ct)
    {
        await _outputEventBus.PublishAsync(new StatusSnapshotEvent
        {
            SessionId = sessionId,
            ChannelId = channelId,
            EventId = Guid.NewGuid().ToString("N"),
            Kind = GameEventKind.StatusSnapshot,
            Visibility = OutputVisibility.System,
            GameId = state?.Id,
            PlayerName = state?.PlayerName,
            ArchetypeId = state?.ArchetypeId,
            ActiveCharacterId = state?.ActiveCharacterId,
            CurrentLocation = state?.CurrentLocation,
            CurrentDay = state?.CurrentDay,
            CurrentMinute = state?.CurrentMinute
        }, ct);
    }

    private async Task RecordAmbientInputAsync(
        SessionInput input,
        IReadOnlyList<SessionAttachmentReference> attachments,
        CancellationToken ct)
    {
        var state = await TryGetStateAsync(input.SessionId, ct);
        if (state is null || string.IsNullOrWhiteSpace(state.ActiveCharacterId))
        {
            await _outputEventBus.PublishAsync(new SystemNoticeEvent
            {
                SessionId = input.SessionId,
                ChannelId = input.ChannelId,
                EventId = Guid.NewGuid().ToString("N"),
                Kind = GameEventKind.SystemNotice,
                Visibility = OutputVisibility.Debug,
                Notice = "环境消息未写入：当前没有激活角色。"
            }, ct);
            return;
        }

        var text = ResolveRawInput(input);
        if (string.IsNullOrWhiteSpace(text))
        {
            await _outputEventBus.PublishAsync(new SystemNoticeEvent
            {
                SessionId = input.SessionId,
                ChannelId = input.ChannelId,
                EventId = Guid.NewGuid().ToString("N"),
                Kind = GameEventKind.SystemNotice,
                Visibility = OutputVisibility.Debug,
                Notice = "环境消息未写入：缺少可记录的文本。"
            }, ct);
            return;
        }

        var context = await _agentContextStore.GetOrCreateAsync(state.Id, state.ActiveCharacterId, ct);
        var speakerName = input.Origin?.UserDisplayName;
        var content = string.IsNullOrWhiteSpace(speakerName) ? text.Trim() : $"{speakerName}: {text.Trim()}";
        context.Conversation.Add(new AgentConversationTurn
        {
            SpeakerId = input.ActorId,
            SpeakerRole = "ambient",
            Content = content,
            TimestampUtc = input.TimestampUtc
        });
        context.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _agentContextStore.SaveAsync(context, ct);

        await _outputEventBus.PublishAsync(new InputAcceptedEvent
        {
            SessionId = input.SessionId,
            ChannelId = input.ChannelId,
            EventId = Guid.NewGuid().ToString("N"),
            Kind = GameEventKind.InputAccepted,
            Visibility = OutputVisibility.Debug,
            RawInput = text.Trim(),
            Attachments = attachments
        }, ct);

        await _outputEventBus.PublishAsync(new SystemNoticeEvent
        {
            SessionId = input.SessionId,
            ChannelId = input.ChannelId,
            EventId = Guid.NewGuid().ToString("N"),
            Kind = GameEventKind.SystemNotice,
            Visibility = OutputVisibility.Debug,
            Notice = $"已记录环境消息到角色上下文：{state.ActiveCharacterId}"
        }, ct);
    }

    private static string ResolveRawInput(SessionInput input)
    {
        if (input.Command is not null)
            return BuildCommandText(input.Command);

        if (!string.IsNullOrWhiteSpace(input.Text))
            return input.Text.Trim();

        return input.Parts
            .Where(p => p.Kind == SessionContentKind.Text && !string.IsNullOrWhiteSpace(p.Text))
            .Select(p => p.Text!.Trim())
            .FirstOrDefault()
            ?? string.Empty;
    }

    private static string BuildCommandText(SessionCommandInvocation command)
    {
        var parts = new List<string> { $"{command.Prefix}{command.Name}" };
        if (command.Args.Count > 0)
            parts.AddRange(command.Args.Where(static x => !string.IsNullOrWhiteSpace(x)));

        foreach (var pair in command.Options)
        {
            parts.Add($"--{pair.Key}");
            if (!string.IsNullOrWhiteSpace(pair.Value))
                parts.Add(pair.Value);
        }

        return string.Join(" ", parts);
    }
}
