namespace openLuo.Modules.Memory.Core.Models;

/// <summary>
/// Memory visibility scope.
/// Current implementation only distinguishes private character memory and shared memory.
/// </summary>
public enum MemoryScope
{
    CharacterPrivate = 0,
    Shared = 1
}
