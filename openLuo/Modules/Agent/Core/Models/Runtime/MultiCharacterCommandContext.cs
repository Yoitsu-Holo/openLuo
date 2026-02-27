using openLuo.Core.Models;
using openLuo.Modules.Commanding.Core.Models;

namespace openLuo.Modules.Agent.Core.Models;

public sealed class MultiCharacterCommandContext
{
    public required InvocationKind Kind { get; init; }

    public required char Prefix { get; init; }

    public required string RawInput { get; init; }

    public required string CommandName { get; init; }

    public required string[] Args { get; init; }

    public required Dictionary<string, string> Options { get; init; }

    public required GameState State { get; init; }

    public required Character ActiveCharacter { get; init; }
}
