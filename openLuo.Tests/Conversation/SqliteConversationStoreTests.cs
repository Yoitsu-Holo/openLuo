using openLuo.AgentContext.Core.Models;
using openLuo.Core.Models;
using openLuo.Infrastructure.Conversation;

namespace openLuo.Tests.Conversation;

public sealed class SqliteConversationStoreTests
{
    // 共享内存数据库：进程内多连接共享同一库（file:...mode=memory&cache=shared）
    private const string ConnectionString = "Data Source=file:convroundtrip?mode=memory&cache=shared";

    private static SqliteConversationStore CreateStore() => new(ConnectionString);

    [Fact]
    public async Task ImageBlock_RoundTrips_WithDataUri()
    {
        var store = CreateStore();
        var block = new ImageBlock
        {
            Kind = BlockKind.Image, AssetId = "a1", MimeType = "image/png",
            DataUri = "data:image/png;base64,iVBORw0KGgo=", Source = BlockSource.User
        };
        await store.AppendAsync(new ConversationTurn
        {
            SessionId = "s1", TurnId = "t1", SpeakerId = "u1", SpeakerName = "u1",
            SpeakerRole = "user", Content = "图", Blocks = [block]
        });

        var turns = await store.GetRecentAsync("s1", 10);

        var restored = Assert.Single(Assert.Single(turns).Blocks!);
        var image = Assert.IsType<ImageBlock>(restored);
        Assert.Equal("a1", image.AssetId);
        Assert.Equal("image/png", image.MimeType);
        Assert.Equal("data:image/png;base64,iVBORw0KGgo=", image.DataUri);
        Assert.Equal(BlockSource.User, image.Source);
    }

    [Fact]
    public async Task AllBlockKinds_RoundTrip_WithConcreteTypes()
    {
        var store = CreateStore();
        var blocks = new List<object>
        {
            new TextBlock { Kind = BlockKind.Text, Text = "hi" },
            new ImageBlock { Kind = BlockKind.Image, AssetId = "a1", MimeType = "image/png" },
            new AssetBlock { Kind = BlockKind.Asset, AssetId = "f1", MimeType = "application/pdf", Name = "doc.pdf" },
            new VideoBlock { Kind = BlockKind.Video, AssetId = "v1", MimeType = "video/mp4", DurationSeconds = 3.5 }
        };
        await store.AppendAsync(new ConversationTurn
        {
            SessionId = "s2", TurnId = "t1", SpeakerId = "u1", SpeakerName = "u1",
            SpeakerRole = "user", Content = "多块", Blocks = blocks
        });

        var turns = await store.GetRecentAsync("s2", 10);

        var restored = Assert.Single(turns).Blocks!;
        Assert.Equal(4, restored.Count);
        Assert.IsType<TextBlock>(restored[0]);
        Assert.IsType<ImageBlock>(restored[1]);
        Assert.IsType<AssetBlock>(restored[2]);
        var video = Assert.IsType<VideoBlock>(restored[3]);
        Assert.Equal(3.5, video.DurationSeconds);
    }

    [Fact]
    public async Task TurnWithoutBlocks_RoundTrips_WithNullBlocks()
    {
        var store = CreateStore();
        await store.AppendAsync(new ConversationTurn
        {
            SessionId = "s3", TurnId = "t1", SpeakerId = "u1", SpeakerName = "u1",
            SpeakerRole = "user", Content = "纯文本"
        });

        var turns = await store.GetRecentAsync("s3", 10);

        Assert.Null(Assert.Single(turns).Blocks);
    }
}
