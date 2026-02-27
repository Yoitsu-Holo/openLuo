using System.Text;

namespace openLuo.Infrastructure.IO;

/// <summary>
/// TUI 模式的 Stream 实现，使用内存缓冲区和事件通知
/// </summary>
public class TuiStreams : Core.Interfaces.IGameStreams
{
    private readonly MemoryStream _inputBuffer = new();
    private readonly MemoryStream _outputBuffer = new();
    private readonly MemoryStream _errorBuffer = new();

    public Stream Input => _inputBuffer;
    public Stream Output => _outputBuffer;
    public Stream Error => _errorBuffer;

    public event Action<string>? OutputReceived;
    public event Action<string>? ErrorReceived;

    public void WriteInput(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text + "\n");
        _inputBuffer.Write(bytes, 0, bytes.Length);
        _inputBuffer.Position = 0;
    }

    public void StartMonitoring()
    {
        Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(50);

                if (_outputBuffer.Length > 0)
                {
                    _outputBuffer.Position = 0;
                    var reader = new StreamReader(_outputBuffer, Encoding.UTF8, leaveOpen: true);
                    var text = await reader.ReadToEndAsync();
                    _outputBuffer.SetLength(0);
                    OutputReceived?.Invoke(text);
                }

                if (_errorBuffer.Length > 0)
                {
                    _errorBuffer.Position = 0;
                    var reader = new StreamReader(_errorBuffer, Encoding.UTF8, leaveOpen: true);
                    var text = await reader.ReadToEndAsync();
                    _errorBuffer.SetLength(0);
                    ErrorReceived?.Invoke(text);
                }
            }
        });
    }
}
