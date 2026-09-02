using System.Text.Json.Nodes;

namespace openLuo.Modules.Llm.Core.Models;

/// <summary>
/// A function-tool declaration sent to the model in the <c>tools</c> array.
/// </summary>
public sealed class LlmToolSpec
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public JsonObject? Parameters { get; init; }
}
