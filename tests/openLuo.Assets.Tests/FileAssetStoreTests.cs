using System.Text;
using openLuo.Infrastructure.Assets;
using Xunit;

namespace openLuo.Assets.Tests;

/// <summary>文件资产存储：内容寻址去重、字节往返、上限、删除、元数据。</summary>
public sealed class FileAssetStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "assets-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private FileAssetStore Create(long maxBytes = 1024) => new(_dir, maxBytes);

    [Fact]
    public async Task Put_IsContentAddressed_AndDeduplicates()
    {
        var store = Create();
        var bytes = Encoding.UTF8.GetBytes("same-content");

        var first = await store.PutAsync(bytes, "text/plain", sessionId: "s1");
        var second = await store.PutAsync(bytes, "text/plain", sessionId: "s2");

        Assert.Equal(first.Id, second.Id);                 // 同内容 → 同 id
        Assert.StartsWith("ast_", first.Id);
        Assert.Equal(bytes.Length, first.Size);
        Assert.NotEmpty(first.Checksum);
    }

    [Fact]
    public async Task Get_ReturnsBytesAndMetadata()
    {
        var store = Create();
        var bytes = Encoding.UTF8.GetBytes("payload-bytes");
        var info = await store.PutAsync(bytes, "application/octet-stream", fileName: "a.bin");

        var blob = await store.GetAsync(info.Id);

        Assert.NotNull(blob);
        Assert.Equal(bytes, blob!.Bytes);
        Assert.Equal("application/octet-stream", blob.Info.Mime);
        Assert.Equal("a.bin", blob.Info.FileName);
        Assert.Equal(info.Checksum, store.Stat(info.Id)!.Checksum);
    }

    [Fact]
    public async Task Put_OverLimit_Throws()
    {
        var store = Create(maxBytes: 8);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.PutAsync(new byte[64], "application/octet-stream"));
        Assert.Contains("exceeds limit", ex.Message);
    }

    [Fact]
    public async Task Put_Empty_Throws()
    {
        var store = Create();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.PutAsync(Array.Empty<byte>(), "text/plain"));
    }

    [Fact]
    public async Task Delete_RemovesAsset_AndGetReturnsNull()
    {
        var store = Create();
        var info = await store.PutAsync(Encoding.UTF8.GetBytes("to-delete"), "text/plain");

        Assert.True(store.Delete(info.Id));
        Assert.Null(store.Stat(info.Id));
        Assert.Null(await store.GetAsync(info.Id));
        Assert.False(store.Delete(info.Id));
    }

    [Fact]
    public async Task Stat_Unknown_ReturnsNull()
    {
        var store = Create();
        Assert.Null(store.Stat("ast_does_not_exist"));
        Assert.Null(await store.GetAsync("ast_does_not_exist"));
    }
}
