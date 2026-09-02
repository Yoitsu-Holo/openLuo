using Microsoft.Data.Sqlite;
using openLuo.AgentContext.Core.Models;
using openLuo.Infrastructure.Conversation;
using Xunit;

namespace openLuo.Cli.Tests;

public sealed class SqliteConversationStoreTests
{
    [Fact]
    public async Task AppendAndGetRecent_RoundTripsTurnsInChronologicalOrder()
    {
        var path = Path.Combine(Path.GetTempPath(), $"openluo-conversation-{Guid.NewGuid():N}.db");
        try
        {
            var store = new SqliteConversationStore($"Data Source={path}");
            await store.AppendAsync(new ConversationTurn
            {
                SessionId = "s", TurnId = "1", SpeakerId = "user", SpeakerRole = "user", Content = "first",
                TimestampUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
            });
            await store.AppendAsync(new ConversationTurn
            {
                SessionId = "s", TurnId = "2", SpeakerId = "agent", SpeakerRole = "outbound", Content = "second",
                TimestampUtc = DateTimeOffset.UtcNow
            });
            var turns = await store.GetRecentAsync("s", 10);
            Assert.Equal(["first", "second"], turns.Select(t => t.Content).ToArray());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }
}
