using openLuo.Modules.Llm.Core.Models;
using openLuo.Modules.Memory.Core.Interfaces;
using openLuo.Modules.Memory.Core.Models;
using LocalChatMessage = openLuo.Modules.Llm.Core.Models.ChatMessage;

namespace openLuo.playgraound.Demos.Executor;

internal static class MemoryRecallDemoSupport
{
    public static IMemoryRecallService CreateRecallService() => new DemoMemoryRecallService();

    public static IReadOnlyList<ChatMessage> BuildConversation() =>
    [
        new LocalChatMessage(ChatMessageRole.User, "外面忽然下雨了。"),
        new LocalChatMessage(ChatMessageRole.Assistant, "先进来吧，莫要在门口继续受风。")
    ];

    private sealed class DemoMemoryRecallService : IMemoryRecallService
    {
        private readonly MemoryRecord[] _records =
        [
            new()
            {
                Id = "m1",
                GameId = "playground-demo",
                OwnerCharacterId = "rin",
                Scope = MemoryScope.CharacterPrivate,
                OccurredAtUtc = DateTime.UtcNow.AddDays(-3),
                SourceText = "主人上次淋雨回家后，汐泠拿来了干毛巾和热茶。",
                Summary = "你记得主人淋雨后容易着凉，你曾主动准备干毛巾和热茶。",
                RecallText = "淋雨 回家 着凉 干毛巾 热茶 照料",
                Tags = ["淋雨", "着凉", "照料", "热茶"],
                Emotion = MemoryEmotion.Positive,
                Importance = 0.8f,
                Salience = 0.9f
            },
            new()
            {
                Id = "m2",
                GameId = "playground-demo",
                OwnerCharacterId = "rin",
                Scope = MemoryScope.CharacterPrivate,
                OccurredAtUtc = DateTime.UtcNow.AddDays(-7),
                SourceText = "主人熬夜后精神不好，汐泠提醒他先喝热茶再说话。",
                Summary = "你记得主人熬夜后状态会变差，最好先安顿身体。",
                RecallText = "熬夜 状态不好 热茶 安顿身体 关心",
                Tags = ["熬夜", "热茶", "关心"],
                Emotion = MemoryEmotion.Positive,
                Importance = 0.6f,
                Salience = 0.6f
            }
        ];

        public Task<MemoryRecallResult> RecallAsync(SemanticRecallQuery query, CancellationToken ct = default)
        {
            var terms = query.QueryTags
                .Append(query.SearchText)
                .Where(static s => !string.IsNullOrWhiteSpace(s))
                .SelectMany(static s => s.Split([' ', '\n', '\r', '\t', '，', '。', '：', ':', '、', ',', '！', '？'], StringSplitOptions.RemoveEmptyEntries))
                .Where(static s => s.Length >= 2)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            var matched = _records
                .Where(record => record.GameId == query.GameId && record.OwnerCharacterId == query.CharacterId)
                .Select(record => new
                {
                    Record = record,
                    Score = terms.Count(term =>
                        record.RecallText.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                        record.Summary.Contains(term, StringComparison.OrdinalIgnoreCase))
                })
                .Where(static x => x.Score > 0)
                .OrderByDescending(static x => x.Score)
                .ThenByDescending(static x => x.Record.Importance)
                .Take(query.TopK)
                .Select(static x => x.Record)
                .ToArray();

            return Task.FromResult(new MemoryRecallResult
            {
                Success = true,
                Records = matched,
                Summary = string.Join("\n", matched.Select(static x => $"- {x.Summary}")),
                Trace = [$"demo-matched:{matched.Length}"],
                Degraded = false
            });
        }
    }
}
