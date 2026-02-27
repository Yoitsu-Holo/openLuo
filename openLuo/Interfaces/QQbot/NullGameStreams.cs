using openLuo.Core.Interfaces;

namespace openLuo.Interfaces.QQbot;

public sealed class NullGameStreams : IGameStreams, IDisposable
{
    private readonly Stream _input = Stream.Null;
    private readonly Stream _output = Console.OpenStandardOutput();
    private readonly Stream _error = Console.OpenStandardError();

    public Stream Input => _input;
    public Stream Output => _output;
    public Stream Error => _error;

    public void Dispose()
    {
        _output.Flush();
        _error.Flush();
    }
}
