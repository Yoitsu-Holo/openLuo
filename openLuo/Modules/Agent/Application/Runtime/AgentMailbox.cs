using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace openLuo.Modules.Agent.Application;

public interface IAgentMailbox
{
    string CharacterId { get; }

    ValueTask EnqueueAsync(AgentDispatchItem item, CancellationToken ct = default);

    IAsyncEnumerable<AgentDispatchItem> ReadAllAsync(CancellationToken ct = default);
}

public sealed class ChannelAgentMailbox : IAgentMailbox
{
    private readonly Channel<AgentDispatchItem> _channel;

    public string CharacterId { get; }

    public ChannelAgentMailbox(string characterId, int capacity = 256)
    {
        CharacterId = characterId;
        var options = new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        };
        _channel = Channel.CreateBounded<AgentDispatchItem>(options);
    }

    public ValueTask EnqueueAsync(AgentDispatchItem item, CancellationToken ct = default) =>
        _channel.Writer.WriteAsync(item, ct);

    public async IAsyncEnumerable<AgentDispatchItem> ReadAllAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        while (await _channel.Reader.WaitToReadAsync(ct))
        {
            while (_channel.Reader.TryRead(out var item))
                yield return item;
        }
    }
}
