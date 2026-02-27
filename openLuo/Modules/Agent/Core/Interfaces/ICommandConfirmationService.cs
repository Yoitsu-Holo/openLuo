namespace openLuo.Modules.Agent.Core.Interfaces;

public interface ICommandConfirmationService
{
    Task<bool> ConfirmAsync(string commandDisplay, string riskLevel, int timeoutSeconds = 30, CancellationToken ct = default);
}
