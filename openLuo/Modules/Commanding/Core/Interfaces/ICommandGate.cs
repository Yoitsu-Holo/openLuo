using openLuo.Modules.Commanding.Core.Models;

namespace openLuo.Modules.Commanding.Core.Interfaces;

/// <summary>Pre/post command execution gate for system-level enforcement.</summary>
public interface ICommandGate
{
    Task<CommandGateBeforeResult> BeforeExecuteAsync(CommandGateContext context, CancellationToken ct = default);

    Task AfterExecuteAsync(CommandGateContext context, CommandResult result, CancellationToken ct = default);
}
