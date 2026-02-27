using System.Text;
using openLuo.Core.Interfaces;
using openLuo.Modules.Agent.Core.Interfaces;
using openLuo.Modules.AppShell.Application;

namespace openLuo.Modules.Gameplay.Application.Services;

public sealed class CommandConfirmationService(IGameStreams streams, IGameLogger logger, IRuntimeConfigCenter? configCenter = null) : ICommandConfirmationService
{
    private readonly IGameStreams _streams = streams;
    private readonly IGameLogger _logger = logger;
    private readonly IRuntimeConfigCenter? _configCenter = configCenter;

    public async Task<bool> ConfirmAsync(string commandDisplay, string riskLevel, int timeoutSeconds = 30, CancellationToken ct = default)
    {
        if (Console.IsInputRedirected)
            return false;

        var prompt = $"\n⚠️  高风险操作 ({riskLevel})：{commandDisplay}\n是否继续执行？[y/N] ";
        var bytes = Encoding.UTF8.GetBytes(prompt);
        await _streams.Output.WriteAsync(bytes, 0, bytes.Length, ct);
        await _streams.Output.FlushAsync(ct);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var fallbackSeconds = Math.Max(5, _configCenter?.GetSnapshot().Agent.InvocationConfirmTimeoutSeconds ?? 30);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, timeoutSeconds > 0 ? timeoutSeconds : fallbackSeconds)));

        var buffer = new byte[256];
        try
        {
            var read = await _streams.Input.ReadAsync(buffer, 0, buffer.Length, timeoutCts.Token);
            if (read <= 0)
                return false;

            var input = Encoding.UTF8.GetString(buffer, 0, read).Trim().ToLowerInvariant();
            var confirmed = input is "y" or "yes";
            _logger.Info("command/confirm", $"{commandDisplay} {(confirmed ? "confirmed" : "denied")}");
            return confirmed;
        }
        catch (OperationCanceledException)
        {
            var timeoutText = Encoding.UTF8.GetBytes("\n（确认超时，已取消执行）\n");
            await _streams.Output.WriteAsync(timeoutText, 0, timeoutText.Length, ct);
            await _streams.Output.FlushAsync(ct);
            _logger.Warn("command/confirm", $"{commandDisplay} timeout");
            return false;
        }
    }
}
