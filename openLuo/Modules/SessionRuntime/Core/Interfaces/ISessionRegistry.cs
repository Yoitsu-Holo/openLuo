using openLuo.Modules.SessionRuntime.Core.Models;

namespace openLuo.Modules.SessionRuntime.Core.Interfaces;

public interface ISessionRegistry
{
    SessionHandle Create(SessionOpenRequest request);

    SessionHandle? Get(string sessionId);

    bool BindGameId(string sessionId, string gameId);

    bool Exists(string sessionId);

    void Remove(string sessionId);
}
