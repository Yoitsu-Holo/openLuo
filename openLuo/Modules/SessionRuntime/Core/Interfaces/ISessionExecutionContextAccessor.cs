using openLuo.Modules.SessionRuntime.Core.Models;

namespace openLuo.Modules.SessionRuntime.Core.Interfaces;

public interface ISessionExecutionContextAccessor
{
    SessionExecutionContext? Current { get; set; }
}
