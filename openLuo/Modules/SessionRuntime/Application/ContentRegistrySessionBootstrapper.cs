using openLuo.Modules.Content.Application.Registry;
using openLuo.Modules.Content.Core.Definitions;
using openLuo.Core.Interfaces;
using openLuo.Core.Models;
using openLuo.Modules.Memory.Core.Interfaces;
using openLuo.Modules.Memory.Core.Models;
using openLuo.Modules.SessionRuntime.Core.Interfaces;
using openLuo.Modules.SessionRuntime.Core.Models;
using openLuo.Modules.WorldState.Core.Interfaces;
using openLuo.Modules.WorldState.Core.Models;

namespace openLuo.Modules.SessionRuntime.Application;

public sealed class ContentRegistrySessionBootstrapper : ISessionBootstrapper
{
    private readonly ContentRegistry _contentRegistry;
    private readonly IGameStateRepository _stateRepo;
    private readonly ICharacterRepository _characterRepo;
    private readonly IStateStore _stateStore;
    private readonly IMemoryWriteService _memoryWriteService;

    public ContentRegistrySessionBootstrapper(ContentRegistry contentRegistry)
        : this(
            contentRegistry,
            SubstituteNotSupported<IGameStateRepository>(),
            SubstituteNotSupported<ICharacterRepository>(),
            SubstituteNotSupported<IStateStore>(),
            SubstituteNotSupported<IMemoryWriteService>())
    {
    }

    public ContentRegistrySessionBootstrapper(
        ContentRegistry contentRegistry,
        IGameStateRepository stateRepo,
        ICharacterRepository characterRepo,
        IStateStore stateStore,
        IMemoryWriteService memoryWriteService)
    {
        _contentRegistry = contentRegistry;
        _stateRepo = stateRepo;
        _characterRepo = characterRepo;
        _stateStore = stateStore;
        _memoryWriteService = memoryWriteService;
    }

    public async Task<SessionBootstrapResult> BootstrapAsync(SessionBootstrapRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SessionId);

        var diagnostics = new List<SessionBootstrapDiagnostic>();
        var characters = ResolveCharacters(request, diagnostics);
        var activeCharacterId = ResolveActiveCharacterId(request, characters, diagnostics);
        var resources = ResolveResources(request, diagnostics);
        var archetypeId = ResolveArchetypeId(activeCharacterId, characters);
        var gameId = request.SessionId;

        await PersistGameStateAsync(gameId, request.PlayerName, archetypeId, activeCharacterId, characters, ct);
        await PersistCharactersAsync(gameId, characters, ct);
        await PersistResourcesAsync(gameId, resources, ct);
        await PersistMemorySeedsAsync(gameId, characters, request, ct);

        return new SessionBootstrapResult
        {
            SessionId = request.SessionId,
            GameId = gameId,
            ArchetypeId = archetypeId,
            Characters = characters,
            ActiveCharacterId = activeCharacterId,
            Resources = resources,
            Diagnostics = diagnostics
        };
    }

    private IReadOnlyList<SessionBootstrapCharacter> ResolveCharacters(
        SessionBootstrapRequest request,
        List<SessionBootstrapDiagnostic> diagnostics)
    {
        var selectedIds = request.SelectedCharacterIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var allCharacterEntries = _contentRegistry.GetByKind(ContentKind.CharacterArchetype);

        var entries = selectedIds.Length == 0
            ? allCharacterEntries
            : selectedIds
                .Select(id =>
                {
                    var entry = allCharacterEntries
                        .FirstOrDefault(candidate => string.Equals(candidate.Id, id, StringComparison.OrdinalIgnoreCase));
                    if (entry is not null)
                        return entry;

                    diagnostics.Add(new SessionBootstrapDiagnostic
                    {
                        Code = "character.not_found",
                        Message = $"Character archetype '{id}' was not found in ContentRegistry."
                    });
                    return null;
                })
                .Where(entry => entry is not null)
                .Cast<RegistryEntry>()
                .ToArray();

        var characters = new List<SessionBootstrapCharacter>();
        foreach (var entry in entries)
        {
            var definition = entry.Definition as CharacterArchetypeDefinition;
            if (definition is null)
                continue;

            characters.Add(new SessionBootstrapCharacter
            {
                CharacterId = BuildCharacterId(definition.Id),
                ArchetypeId = definition.Id,
                DisplayName = ResolveCharacterName(definition),
                InitialLocation = definition.InitialLocation,
                SourcePack = entry.SourcePack
            });
        }

        if (characters.Count == 0 && selectedIds.Length > 0)
        {
            foreach (var selectedId in selectedIds)
            {
                characters.Add(new SessionBootstrapCharacter
                {
                    CharacterId = BuildCharacterId(selectedId),
                    ArchetypeId = selectedId,
                    DisplayName = "未知",
                    InitialLocation = string.Empty,
                    SourcePack = "bootstrap:fallback"
                });
            }
        }

        return characters;
    }

    private string? ResolveActiveCharacterId(
        SessionBootstrapRequest request,
        IReadOnlyList<SessionBootstrapCharacter> characters,
        List<SessionBootstrapDiagnostic> diagnostics)
    {
        if (characters.Count == 0)
            return null;

        if (!string.IsNullOrWhiteSpace(request.ActiveCharacterId))
        {
            var requested = request.ActiveCharacterId.Trim();
            if (characters.Any(x => string.Equals(x.CharacterId, requested, StringComparison.OrdinalIgnoreCase)))
                return characters.First(x => string.Equals(x.CharacterId, requested, StringComparison.OrdinalIgnoreCase)).CharacterId;

            diagnostics.Add(new SessionBootstrapDiagnostic
            {
                Code = "active_character.not_found",
                Message = $"Active character '{requested}' was not materialized by bootstrap."
            });
        }

        return characters[0].CharacterId;
    }

    private IReadOnlyDictionary<string, SessionBootstrapResourceState> ResolveResources(
        SessionBootstrapRequest request,
        List<SessionBootstrapDiagnostic> diagnostics)
    {
        var resources = new Dictionary<string, SessionBootstrapResourceState>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in _contentRegistry.GetByKind(ContentKind.Resource))
        {
            if (entry.Definition is not ResourceDefinition definition)
                continue;

            var hasOverride = request.ResourceOverrides.TryGetValue(definition.Id, out var overrideValue);
            var value = hasOverride ? overrideValue : definition.InitialValue;
            var outOfRange = IsOutOfRange(value, definition);
            if (outOfRange)
            {
                diagnostics.Add(new SessionBootstrapDiagnostic
                {
                    Code = "resource.override_out_of_range",
                    Message = $"Resource '{definition.Id}' resolved to '{value}', outside declared range."
                });
            }

            resources[definition.Id] = new SessionBootstrapResourceState
            {
                ResourceId = definition.Id,
                DisplayName = string.IsNullOrWhiteSpace(definition.DisplayName) ? definition.Id : definition.DisplayName,
                ResourceType = definition.ResourceType,
                Value = value,
                MinValue = definition.MinValue,
                MaxValue = definition.MaxValue,
                OwnerKind = definition.Metadata.TryGetValue("owner_kind", out var ownerKind) ? ownerKind : string.Empty,
                SourcePack = entry.SourcePack
            };
        }

        foreach (var overrideEntry in request.ResourceOverrides)
        {
            if (resources.ContainsKey(overrideEntry.Key))
                continue;

            diagnostics.Add(new SessionBootstrapDiagnostic
            {
                Code = "resource.not_found",
                Message = $"Resource override '{overrideEntry.Key}' did not match any registered resource."
            });
        }

        return resources;
    }

    private static string ResolveCharacterName(CharacterArchetypeDefinition definition)
    {
        if (!string.IsNullOrWhiteSpace(definition.CharacterName))
            return definition.CharacterName;

        if (!string.IsNullOrWhiteSpace(definition.DisplayName))
            return definition.DisplayName;

        return definition.Id;
    }

    private static bool IsOutOfRange(decimal? value, ResourceDefinition definition)
    {
        if (!value.HasValue)
            return false;

        if (definition.MinValue.HasValue && value.Value < definition.MinValue.Value)
            return true;

        if (definition.MaxValue.HasValue && value.Value > definition.MaxValue.Value)
            return true;

        return false;
    }

    private static string ResolveArchetypeId(string? activeCharacterId, IReadOnlyList<SessionBootstrapCharacter> characters)
    {
        if (string.IsNullOrWhiteSpace(activeCharacterId))
            return characters.FirstOrDefault()?.ArchetypeId ?? string.Empty;

        return characters.FirstOrDefault(x => string.Equals(x.CharacterId, activeCharacterId, StringComparison.OrdinalIgnoreCase))?.ArchetypeId
            ?? characters.FirstOrDefault()?.ArchetypeId
            ?? string.Empty;
    }

    private async Task PersistGameStateAsync(
        string gameId,
        string playerName,
        string archetypeId,
        string? activeCharacterId,
        IReadOnlyList<SessionBootstrapCharacter> characters,
        CancellationToken ct)
    {
        var currentLocation = characters.FirstOrDefault(x => string.Equals(x.CharacterId, activeCharacterId, StringComparison.OrdinalIgnoreCase))?.InitialLocation
            ?? characters.FirstOrDefault()?.InitialLocation
            ?? string.Empty;

        await _stateRepo.SaveAsync(new GameState
        {
            Id = gameId,
            PlayerName = playerName,
            ArchetypeId = archetypeId,
            ActiveCharacterId = activeCharacterId ?? string.Empty,
            CurrentLocation = currentLocation,
            CurrentDay = 1,
            LastInteractionDay = 1,
            CurrentMinute = 480
        }, ct);
    }

    private async Task PersistCharactersAsync(string gameId, IReadOnlyList<SessionBootstrapCharacter> characters, CancellationToken ct)
    {
        for (var i = 0; i < characters.Count; i++)
        {
            var character = characters[i];
            await _characterRepo.SaveAsync(new Character
            {
                Id = character.CharacterId,
                GameId = gameId,
                ArchetypeId = character.ArchetypeId,
                Name = character.DisplayName,
                DisplayPriority = i + 1,
                IsEnabled = true
            }, ct);
        }
    }

    private async Task PersistResourcesAsync(string gameId, IReadOnlyDictionary<string, SessionBootstrapResourceState> resources, CancellationToken ct)
    {
        var batch = new List<(string gameId, StateOwnerKind ownerKind, string ownerId, string @namespace, string key, string value)>();

        // Character-scoped resources apply to every materialized character. Game-scoped resources apply once.
        var characters = await _characterRepo.ListByGameIdAsync(gameId, ct);
        foreach (var resource in resources.Values)
        {
            if (!resource.Value.HasValue)
                continue;

            if (!TryParseResourceId(resource.ResourceId, out var ns, out var key))
                continue;

            if (!TryMapOwnerKind(resource.OwnerKind, out var ownerKind))
                continue;

            switch (ownerKind)
            {
                case StateOwnerKind.Character:
                    foreach (var character in characters)
                        batch.Add((gameId, ownerKind, character.Id, ns, key, resource.Value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                    break;
                case StateOwnerKind.Game:
                    batch.Add((gameId, ownerKind, gameId, ns, key, resource.Value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                    break;
            }
        }

        if (batch.Count > 0)
            await _stateStore.SetBatchAsync(batch);
    }

    private async Task PersistMemorySeedsAsync(
        string gameId,
        IReadOnlyList<SessionBootstrapCharacter> characters,
        SessionBootstrapRequest request,
        CancellationToken ct)
    {
        foreach (var memory in request.SharedMemorySeeds.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            await _memoryWriteService.WriteAsync(new MemoryWriteInput
            {
                GameId = gameId,
                CharacterId = string.Empty,
                Scope = MemoryScope.Shared,
                RawContent = memory.Trim(),
                Source = "session/bootstrap"
            }, ct);
        }

        foreach (var character in characters)
        {
            var archetype = _contentRegistry.TryGet<CharacterArchetypeDefinition>(character.ArchetypeId, out var definition)
                ? definition
                : null;

            if (!string.IsNullOrWhiteSpace(archetype?.Backstory))
            {
                await _memoryWriteService.WriteAsync(new MemoryWriteInput
                {
                    GameId = gameId,
                    CharacterId = character.CharacterId,
                    Scope = MemoryScope.CharacterPrivate,
                    RawContent = archetype.Backstory.Trim(),
                    Source = "session/bootstrap"
                }, ct);
            }

            if (request.PrivateMemorySeedsByCharacter.TryGetValue(character.ArchetypeId, out var privateSeeds) ||
                request.PrivateMemorySeedsByCharacter.TryGetValue(character.CharacterId, out privateSeeds))
            {
                foreach (var memory in privateSeeds.Where(x => !string.IsNullOrWhiteSpace(x)))
                {
                    await _memoryWriteService.WriteAsync(new MemoryWriteInput
                    {
                        GameId = gameId,
                        CharacterId = character.CharacterId,
                        Scope = MemoryScope.CharacterPrivate,
                        RawContent = memory.Trim(),
                        Source = "session/bootstrap"
                    }, ct);
                }
            }
        }
    }

    private static bool TryParseResourceId(string resourceId, out string ns, out string key)
    {
        var index = resourceId.IndexOf('.');
        if (index <= 0 || index >= resourceId.Length - 1)
        {
            ns = string.Empty;
            key = string.Empty;
            return false;
        }

        ns = resourceId[..index];
        key = resourceId[(index + 1)..];
        return true;
    }

    private static bool TryMapOwnerKind(string ownerKind, out StateOwnerKind mapped)
    {
        mapped = ownerKind.Trim().ToLowerInvariant() switch
        {
            "character" => StateOwnerKind.Character,
            "game" => StateOwnerKind.Game,
            _ => StateOwnerKind.System
        };

        return mapped is StateOwnerKind.Character or StateOwnerKind.Game;
    }

    private static string BuildCharacterId(string archetypeId) => $"char_{archetypeId.Trim().ToLowerInvariant()}";

    private static T SubstituteNotSupported<T>() where T : class =>
        throw new InvalidOperationException($"This ContentRegistrySessionBootstrapper constructor is for tests only. Missing dependency: {typeof(T).Name}");
}
