using openLuo.Core.Interfaces;

namespace openLuo.Infrastructure.IO;

public class ConsoleStreams : IGameStreams
{
    private readonly Stream _input = Console.OpenStandardInput();
    private readonly Stream _output = Console.OpenStandardOutput();
    private readonly Stream _error = Console.OpenStandardError();

    public Stream Input => _input;
    public Stream Output => _output;
    public Stream Error => _error;
}
