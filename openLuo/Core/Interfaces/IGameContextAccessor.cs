using openLuo.Core.Models;

namespace openLuo.Core.Interfaces;

public interface IGameContextAccessor
{
    GameRuntimeContext? Current { get; set; }
}
