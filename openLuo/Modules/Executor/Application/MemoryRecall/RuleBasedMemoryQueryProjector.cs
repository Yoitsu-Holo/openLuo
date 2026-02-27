using System.Text;
using openLuo.Modules.Memory.Core.Models;

namespace openLuo.Modules.Executor.Application.MemoryRecall;

public sealed class RuleBasedMemoryQueryProjector : IMemoryQueryProjector
{
    public Task<SemanticRecallQuery> ProjectAsync(MemoryRecallInput input, CancellationToken ct = default)
    {
        var tags = ExtractTags(input);
        var scopes = new List<MemoryScope>();
        if (input.Options.IncludeCharacterPrivateMemory)
            scopes.Add(MemoryScope.CharacterPrivate);
        if (input.Options.IncludeSharedMemory)
            scopes.Add(MemoryScope.Shared);

        var searchBuilder = new StringBuilder();
        if (input.RecentConversation.Count > 0)
        {
            foreach (var message in input.RecentConversation.TakeLast(2))
            {
                if (string.IsNullOrWhiteSpace(message.Content))
                    continue;
                searchBuilder.AppendLine(message.Content.Trim());
            }
        }

        if (!string.IsNullOrWhiteSpace(input.SceneState))
            searchBuilder.AppendLine(input.SceneState.Trim());

        if (!string.IsNullOrWhiteSpace(input.CurrentGoal))
            searchBuilder.AppendLine(input.CurrentGoal.Trim());

        if (!string.IsNullOrWhiteSpace(input.PlayerInput))
            searchBuilder.AppendLine(input.PlayerInput.Trim());

        return Task.FromResult(new SemanticRecallQuery
        {
            GameId = input.GameId,
            CharacterId = input.CharacterId,
            SearchText = searchBuilder.ToString().Trim(),
            QueryTags = tags,
            Scopes = scopes,
            PreferRecent = input.Options.PreferRecent,
            PreferImportant = input.Options.PreferImportant,
            PreferEmotionallySalient = input.Options.PreferEmotionallySalient,
            TopK = input.Options.TopK,
            Reason = "Built from recent conversation, scene context, and current player input."
        });
    }

    private static IReadOnlyList<string> ExtractTags(MemoryRecallInput input)
    {
        var seeds = new[]
        {
            input.PlayerInput,
            input.SceneState,
            input.CurrentGoal
        };

        var tags = seeds
            .Where(static s => !string.IsNullOrWhiteSpace(s))
            .SelectMany(static s => s.Split([' ', '\n', '\r', '\t', '，', '。', '：', ':', '、', ',', '！', '？'], StringSplitOptions.RemoveEmptyEntries))
            .Select(static token => token.Trim())
            .Where(static token => token.Length >= 2)
            .Distinct(StringComparer.Ordinal)
            .Take(8)
            .ToList();

        foreach (var hint in DeriveSemanticHints(input))
        {
            if (tags.Contains(hint, StringComparer.Ordinal))
                continue;
            tags.Add(hint);
        }

        return tags.Take(12).ToArray();
    }

    private static IEnumerable<string> DeriveSemanticHints(MemoryRecallInput input)
    {
        var corpus = string.Concat(input.PlayerInput, "\n", input.SceneState, "\n", input.CurrentGoal);

        if (corpus.Contains('雨', StringComparison.Ordinal))
        {
            yield return "下雨";
            yield return "淋雨";
        }

        if (corpus.Contains('湿', StringComparison.Ordinal))
        {
            yield return "淋湿";
            yield return "衣服湿";
        }

        if (corpus.Contains("冷", StringComparison.Ordinal) || corpus.Contains("寒", StringComparison.Ordinal))
            yield return "着凉";
    }
}
