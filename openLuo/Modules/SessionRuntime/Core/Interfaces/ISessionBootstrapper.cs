using openLuo.Modules.SessionRuntime.Core.Models;

namespace openLuo.Modules.SessionRuntime.Core.Interfaces;

public interface ISessionBootstrapper
{
    Task<SessionBootstrapResult> BootstrapAsync(SessionBootstrapRequest request, CancellationToken ct = default);
}
